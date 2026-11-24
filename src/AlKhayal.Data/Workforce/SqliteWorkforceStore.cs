using System;
using System.Collections.Generic;
using System.Data.SQLite;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using AlKhayal.Application.Workforce;
using AlKhayal.Application.Treasury;
using AlKhayal.Data.Treasury;
using Dapper;

namespace AlKhayal.Data.Workforce
{
    public sealed class SqliteWorkforceStore : IWorkforceStore
    {
        private readonly string _connectionString;
        public SqliteWorkforceStore(string connectionString) { _connectionString = connectionString; }
        private static string Now() => DateTime.UtcNow.ToString("o", CultureInfo.InvariantCulture);
        private static CommandDefinition Cmd(string sql, object p, SQLiteTransaction tx, CancellationToken ct) => new CommandDefinition(sql, p, tx, cancellationToken: ct);
        private async Task<SQLiteConnection> OpenAsync(CancellationToken ct)
        {
            var c = new SQLiteConnection(_connectionString);
            try { await c.OpenAsync(ct).ConfigureAwait(false); await c.ExecuteAsync(Cmd("PRAGMA foreign_keys=ON;", null, null, ct)).ConfigureAwait(false); return c; }
            catch { c.Dispose(); throw; }
        }
        private static WorkforceException Conflict(string message) => new WorkforceException("conflict", message);
        private static WorkforceException Missing() => new WorkforceException("not_found", "السجل غير موجود أو غير متاح لك.");
        public async Task<long> ExecuteAsync(string action, WorkforceCommand x, long? amount, string hash, long actorId, bool administrator, CancellationToken ct)
        {
            using (var c = await OpenAsync(ct).ConfigureAwait(false))
            using (var tx = c.BeginTransaction())
            {
                var prior = await c.QuerySingleOrDefaultAsync<Operation>(Cmd("SELECT RequestHash,EntityId FROM WorkforceOperations WHERE OperationKey=@OperationKey", new { x.OperationKey }, tx, ct)).ConfigureAwait(false);
                if (prior != null) { if (prior.RequestHash != hash) throw Conflict("رقم العملية مستخدم ببيانات أخرى."); tx.Commit(); return prior.EntityId; }
                var feature = action.StartsWith("attendance-", StringComparison.Ordinal) ? "employee-attendance" : action.StartsWith("employee-", StringComparison.Ordinal) || action == "advance-add" || action == "payroll-pay" ? "employee-management" : null;
                if (feature != null && await c.ExecuteScalarAsync<long>(Cmd("SELECT COUNT(*) FROM ModuleConfiguration WHERE ModuleKey=@feature AND IsEnabled=0", new { feature }, tx, ct)).ConfigureAwait(false) != 0)
                    throw Conflict("تم تعطيل الوحدة أثناء العمل. حدّث الصفحة.");
                var now = Now(); long id;
                var detail = x.Reason ?? "";
                if (action == "employee-save")
                {
                    if (!string.IsNullOrWhiteSpace(x.NationalId) && await c.ExecuteScalarAsync<long>(Cmd("SELECT COUNT(*) FROM Employees WHERE NationalId=@NationalId AND Id<>@Id", new { x.NationalId, x.Id }, tx, ct)).ConfigureAwait(false) != 0) throw Conflict("الرقم القومي مسجل لموظف آخر.");
                    if (x.LinkedUserId.HasValue)
                    {
                        if (await c.ExecuteScalarAsync<long>(Cmd("SELECT COUNT(*) FROM Users WHERE Id=@LinkedUserId", new { x.LinkedUserId }, tx, ct)).ConfigureAwait(false) != 1) throw Missing();
                        if (await c.ExecuteScalarAsync<long>(Cmd("SELECT COUNT(*) FROM Employees WHERE LinkedUserId=@LinkedUserId AND Id<>@Id", new { x.LinkedUserId, x.Id }, tx, ct)).ConfigureAwait(false) != 0) throw Conflict("حساب الدخول مرتبط بموظف آخر.");
                    }
                    if (x.Id > 0)
                    {
                        var before = await c.QuerySingleOrDefaultAsync<Employee>(Cmd("SELECT * FROM Employees WHERE Id=@Id", new { x.Id }, tx, ct)).ConfigureAwait(false);
                        if (before == null) throw Missing();
                        if (before.Version != x.Version) throw Conflict("تغير سجل الموظف. حدّث البيانات قبل الحفظ.");
                        if ((!x.IsActive || !x.AttendanceEnabled) && await c.ExecuteScalarAsync<long>(Cmd("SELECT COUNT(*) FROM EmployeeAttendance WHERE EmployeeId=@Id AND CheckOutUtc IS NULL", new { x.Id }, tx, ct)).ConfigureAwait(false) > 0) throw Conflict("سجّل الانصراف قبل تعطيل الموظف أو الحضور.");
                        detail = "before=" + EmployeeAudit(before);
                    }
                    var p = new { x.Id, x.Version, x.Name, x.Phone, x.JobTitle, x.HireDate, x.Address, x.NationalId, x.Notes, x.LinkedUserId, x.IsActive, x.AttendanceEnabled, SalaryScaled = amount, x.SalaryFrequency, x.Payday, x.CompensationNotes, now };
                    if (x.Id == 0)
                        id = await c.ExecuteScalarAsync<long>(Cmd(@"INSERT INTO Employees(Name,Phone,JobTitle,HireDate,Address,NationalId,Notes,LinkedUserId,IsActive,AttendanceEnabled,SalaryScaled,SalaryFrequency,Payday,CompensationNotes,CreatedUtc,UpdatedUtc)
                            VALUES(@Name,@Phone,@JobTitle,@HireDate,@Address,@NationalId,@Notes,@LinkedUserId,@IsActive,@AttendanceEnabled,@SalaryScaled,@SalaryFrequency,@Payday,@CompensationNotes,@now,@now); SELECT last_insert_rowid();", p, tx, ct)).ConfigureAwait(false);
                    else
                    {
                        await c.ExecuteAsync(Cmd(@"UPDATE Employees SET Name=@Name,Phone=@Phone,JobTitle=@JobTitle,HireDate=@HireDate,Address=@Address,NationalId=@NationalId,Notes=@Notes,LinkedUserId=@LinkedUserId,IsActive=@IsActive,AttendanceEnabled=@AttendanceEnabled,SalaryScaled=@SalaryScaled,SalaryFrequency=@SalaryFrequency,Payday=@Payday,CompensationNotes=@CompensationNotes,UpdatedUtc=@now,Version=Version+1 WHERE Id=@Id AND Version=@Version", p, tx, ct)).ConfigureAwait(false);
                        id = x.Id;
                    }
                    detail += " after=" + EmployeeAudit(await c.QuerySingleAsync<Employee>(Cmd("SELECT * FROM Employees WHERE Id=@id", new { id }, tx, ct)).ConfigureAwait(false));
                }
                else if (action == "employee-archive")
                {
                    var employee = await c.QuerySingleOrDefaultAsync<Employee>(Cmd("SELECT * FROM Employees WHERE Id=@Id", new { x.Id }, tx, ct)).ConfigureAwait(false);
                    if (employee == null) throw Missing();
                    if (employee.Version != x.Version) throw Conflict("تغير سجل الموظف. حدّث البيانات قبل الأرشفة.");
                    if (await c.ExecuteScalarAsync<long>(Cmd("SELECT COUNT(*) FROM EmployeeAttendance WHERE EmployeeId=@Id AND CheckOutUtc IS NULL", new { x.Id }, tx, ct)).ConfigureAwait(false) > 0) throw Conflict("سجّل الانصراف قبل أرشفة الموظف.");
                    await c.ExecuteAsync(Cmd("UPDATE Employees SET IsActive=0,AttendanceEnabled=0,UpdatedUtc=@now,Version=Version+1 WHERE Id=@Id AND Version=@Version", new { x.Id, x.Version, now }, tx, ct)).ConfigureAwait(false);
                    id = x.Id; detail = "archived=" + EmployeeAudit(employee);
                }
                else if (action == "advance-add")
                {
                    if (await c.ExecuteScalarAsync<long>(Cmd("SELECT COUNT(*) FROM Employees WHERE Id=@EmployeeId AND IsActive=1", new { x.EmployeeId }, tx, ct)).ConfigureAwait(false) != 1) throw Conflict("الموظف غير نشط أو غير موجود.");
                    var movement = await SqliteTreasuryStore.PostWithinTransactionAsync(c, tx,
                        new TreasuryMovementCommand { AccountId = x.TreasuryAccountId, Amount = AlKhayal.Application.Finance.MoneyPolicy.FormatForUser(amount.Value), Notes = "سلفة موظف " + x.EmployeeId, OperationKey = "advance:" + x.OperationKey },
                        "withdraw", amount.Value, hash, actorId, ct).ConfigureAwait(false);
                    id = await c.ExecuteScalarAsync<long>(Cmd("INSERT INTO EmployeeAdvances(EmployeeId,AmountScaled,CreatedUtc,TreasuryMovementId,ActorUserId,OperationKey,Notes) VALUES(@EmployeeId,@amount,@now,@TreasuryMovementId,@actorId,@OperationKey,@Notes); SELECT last_insert_rowid();", new { x.EmployeeId, amount, now, TreasuryMovementId = movement.Id, actorId, x.OperationKey, x.Notes }, tx, ct)).ConfigureAwait(false);
                    detail = "employee=" + x.EmployeeId + ";amount=" + amount;
                }
                else if (action == "payroll-pay")
                {
                    var employee = await c.QuerySingleOrDefaultAsync<Employee>(Cmd("SELECT * FROM Employees WHERE Id=@EmployeeId AND IsActive=1", new { x.EmployeeId }, tx, ct)).ConfigureAwait(false);
                    if (employee == null || !employee.SalaryScaled.HasValue || employee.SalaryFrequency != "monthly") throw Conflict("الموظف غير نشط أو لم يُحدد له راتب شهري.");
                    if (await c.ExecuteScalarAsync<long>(Cmd("SELECT COUNT(*) FROM TreasuryAccounts WHERE Id=@TreasuryAccountId AND IsDeleted=0", new { x.TreasuryAccountId }, tx, ct)).ConfigureAwait(false) != 1) throw Conflict("الخزنة أو البنك المحدد غير موجود.");
                    if (await c.ExecuteScalarAsync<long>(Cmd(PaidPeriod, new { x.EmployeeId, x.Period }, tx, ct)).ConfigureAwait(false) != 0) throw Conflict("تم صرف راتب الموظف عن هذه الفترة بالفعل.");
                    var advances = await c.ExecuteScalarAsync<long>(Cmd(UnsettledAdvanceTotal, new { x.EmployeeId }, tx, ct)).ConfigureAwait(false);
                    if (advances > employee.SalaryScaled.Value) throw Conflict("إجمالي السلف يتجاوز الراتب. راجع السلف قبل الصرف.");
                    var netPay = employee.SalaryScaled.Value - advances;
                    var treasuryMovementId = (long?)null;
                    if (netPay > 0)
                    {
                        var movement = await SqliteTreasuryStore.PostWithinTransactionAsync(c, tx,
                            new TreasuryMovementCommand { AccountId = x.TreasuryAccountId, Amount = AlKhayal.Application.Finance.MoneyPolicy.FormatForUser(netPay), Notes = "صرف راتب " + employee.Name + " عن " + x.Period, OperationKey = "payroll:" + x.OperationKey },
                            "withdraw", netPay, hash, actorId, ct).ConfigureAwait(false);
                        treasuryMovementId = movement.Id;
                    }
                    id = await c.ExecuteScalarAsync<long>(Cmd("INSERT INTO EmployeePayrollPayments(EmployeeId,SalaryScaled,AdvancesScaled,NetScaled,PaidUtc,Period,TreasuryMovementId,ActorUserId,OperationKey,Notes) VALUES(@EmployeeId,@salary,@advances,@net,@now,@Period,@treasuryMovementId,@actorId,@OperationKey,@Notes); SELECT last_insert_rowid();", new { x.EmployeeId, salary = employee.SalaryScaled.Value, advances, net = netPay, now, x.Period, treasuryMovementId, actorId, x.OperationKey, x.Notes }, tx, ct)).ConfigureAwait(false);
                    await c.ExecuteAsync(Cmd("INSERT INTO EmployeePayrollAdvanceAllocations(AdvanceId,PayrollPaymentId,AmountScaled) SELECT a.Id,@payrollId,a.AmountScaled FROM EmployeeAdvances a WHERE a.EmployeeId=@EmployeeId AND NOT EXISTS(SELECT 1 FROM EmployeePayrollAdvanceAllocations l WHERE l.AdvanceId=a.Id)", new { payrollId = id, x.EmployeeId }, tx, ct)).ConfigureAwait(false);
                    detail = "employee=" + x.EmployeeId + ";period=" + x.Period + ";salary=" + employee.SalaryScaled.Value + ";advances=" + advances;
                }
                else if (action.StartsWith("attendance-", StringComparison.Ordinal))
                {
                    if (action == "attendance-in")
                    {
                        if (await c.ExecuteScalarAsync<long>(Cmd("SELECT COUNT(*) FROM Employees WHERE Id=@EmployeeId AND IsActive=1 AND AttendanceEnabled=1", new { x.EmployeeId }, tx, ct)).ConfigureAwait(false) != 1) throw Conflict("الحضور غير مفعل لهذا الموظف أو أن الموظف غير نشط.");
                        await CheckOverlap(c, tx, x.EmployeeId, 0, now, null, ct).ConfigureAwait(false);
                        id = await c.ExecuteScalarAsync<long>(Cmd("INSERT INTO EmployeeAttendance(EmployeeId,CheckInUtc) VALUES(@EmployeeId,@now); SELECT last_insert_rowid();", new { x.EmployeeId, now }, tx, ct)).ConfigureAwait(false);
                        detail = now;
                    }
                    else if (action == "attendance-break")
                    {
                        var before = await c.QuerySingleOrDefaultAsync<AttendanceRecord>(Cmd("SELECT * FROM EmployeeAttendance WHERE Id=@Id", new { x.Id }, tx, ct)).ConfigureAwait(false);
                        if (before == null || before.CheckOutUtc != null) throw Conflict("لا يمكن تسجيل بريك بعد الانصراف أو لسجل غير موجود.");
                        var totalBreak = checked(before.BreakMinutes + x.BreakMinutes);
                        if (totalBreak > 1440) throw Conflict("إجمالي وقت البريك لا يمكن أن يتجاوز 1440 دقيقة.");
                        await c.ExecuteAsync(Cmd("UPDATE EmployeeAttendance SET BreakMinutes=@totalBreak,Version=Version+1 WHERE Id=@Id AND CheckOutUtc IS NULL", new { totalBreak, x.Id }, tx, ct)).ConfigureAwait(false);
                        id = x.Id; detail = "break-added=" + x.BreakMinutes + ";break-total=" + totalBreak;
                    }
                    else
                    {
                        var before = await c.QuerySingleOrDefaultAsync<AttendanceRecord>(Cmd("SELECT * FROM EmployeeAttendance WHERE Id=@Id", new { x.Id }, tx, ct)).ConfigureAwait(false);
                        if (before == null) throw Missing();
                        if (action == "attendance-out" && before.CheckOutUtc != null) throw Conflict("تم تسجيل الانصراف بالفعل.");
                        if (action == "attendance-correct" && before.Version != x.Version) throw Conflict("تغير سجل الحضور. حدّث البيانات.");
                        var start = action == "attendance-out" ? before.CheckInUtc : x.CheckInUtc;
                        var end = action == "attendance-out" ? now : x.CheckOutUtc;
                        var breakMinutes = action == "attendance-out" ? checked(before.BreakMinutes + x.BreakMinutes) : before.BreakMinutes;
                        if (breakMinutes > 1440) throw Conflict("إجمالي وقت البريك لا يمكن أن يتجاوز 1440 دقيقة.");
                        if (end != null && string.CompareOrdinal(end, start) <= 0) throw Conflict("الانصراف يجب أن يكون بعد الحضور.");
                        await CheckOverlap(c, tx, before.EmployeeId, before.Id, start, end, ct).ConfigureAwait(false);
                        await c.ExecuteAsync(Cmd("UPDATE EmployeeAttendance SET CheckInUtc=@start,CheckOutUtc=@end,BreakMinutes=@breakMinutes,Version=Version+1 WHERE Id=@Id", new { start, end, breakMinutes, x.Id }, tx, ct)).ConfigureAwait(false);
                        id = x.Id;
                        detail += " before=" + before.CheckInUtc + "/" + before.CheckOutUtc + " after=" + start + "/" + end;
                    }
                }
                else if (action == "shift-open")
                {
                    if (await c.ExecuteScalarAsync<long>(Cmd("SELECT COUNT(*) FROM Users WHERE Id=@CashierId AND IsActive=1 AND Role IN ('cashier','administrator')", new { x.CashierId }, tx, ct)).ConfigureAwait(false) != 1) throw Missing();
                    // Standalone has exactly one supported register. Do not trust a browser-provided device ID.
                    if (await c.ExecuteScalarAsync<long>(Cmd("SELECT COUNT(*) FROM CashierShifts WHERE ClosedUtc IS NULL AND (CashierId=@CashierId OR RegisterId='standalone')", new { x.CashierId }, tx, ct)).ConfigureAwait(false) > 0) throw Conflict("توجد وردية مفتوحة للكاشير أو لهذا الجهاز. أغلقها أولًا.");
                    id = await c.ExecuteScalarAsync<long>(Cmd("INSERT INTO CashierShifts(CashierId,RegisterId,OpenedUtc,OpenedBy,OpeningScaled,OpeningNotes) VALUES(@CashierId,'standalone',@now,@actorId,@amount,@Notes); SELECT last_insert_rowid();", new { x.CashierId, now, actorId, amount, x.Notes }, tx, ct)).ConfigureAwait(false);
                    detail = "opening=" + amount + ";cashier=" + x.CashierId;
                }
                else
                {
                    var shift = await c.QuerySingleOrDefaultAsync<CashierShift>(Cmd(ShiftSql + " WHERE s.Id=@Id", new { x.Id }, tx, ct)).ConfigureAwait(false);
                    if (shift == null || !administrator && shift.CashierId != actorId) throw Missing();
                    if (shift.ClosedUtc != null) throw Conflict("الوردية مغلقة ولا تقبل معاملات جديدة.");
                    if (action == "shift-close")
                    {
                        if (x.ExpectedScaled.HasValue && x.ExpectedScaled.Value != shift.ExpectedScaled) throw Conflict("تغير النقد المتوقع. حدّث الوردية وأعد الجرد.");
                        await c.ExecuteAsync(Cmd("UPDATE CashierShifts SET ClosedUtc=@now,ClosedBy=@actorId,CountedScaled=@amount,ClosingExpectedScaled=@expected,ClosingNotes=@Notes WHERE Id=@Id AND ClosedUtc IS NULL", new { now, actorId, amount, expected = shift.ExpectedScaled, x.Notes, x.Id }, tx, ct)).ConfigureAwait(false);
                        detail = "expected=" + shift.ExpectedScaled + ";counted=" + amount;
                    }
                    else
                    {
                        var expected = x.Direction == "in" ? checked(shift.ExpectedScaled + amount.Value) : checked(shift.ExpectedScaled - amount.Value);
                        if (expected < 0) throw Conflict("المبلغ يتجاوز النقد المتوقع في الوردية.");
                        await c.ExecuteAsync(Cmd("INSERT INTO CashierShiftEvents(ShiftId,Kind,AmountScaled,ActorUserId,Reason,CreatedUtc) VALUES(@Id,@Direction,@amount,@actorId,@Reason,@now)", new { x.Id, x.Direction, amount, actorId, x.Reason, now }, tx, ct)).ConfigureAwait(false);
                        detail += ";direction=" + x.Direction + ";amount=" + amount;
                    }
                    id = x.Id;
                }
                await c.ExecuteAsync(Cmd("INSERT INTO WorkforceOperations(OperationKey,Action,RequestHash,EntityId,ActorUserId,CreatedUtc) VALUES(@OperationKey,@action,@hash,@id,@actorId,@now); INSERT INTO WorkforceAudit(Action,EntityId,ActorUserId,Detail,CreatedUtc) VALUES(@action,@id,@actorId,@detail,@now)", new { x.OperationKey, action, hash, id, actorId, now, detail }, tx, ct)).ConfigureAwait(false);
                tx.Commit(); return id;
            }
        }
        private static string EmployeeAudit(Employee e) => string.Join("|", new object[] { e.Version, e.Name, e.Phone, e.JobTitle, e.HireDate, e.Address, e.NationalId, e.Notes, e.LinkedUserId, e.IsActive, e.AttendanceEnabled, e.Salary, e.SalaryFrequency, e.Payday, e.CompensationNotes });
        private static async Task CheckOverlap(SQLiteConnection c, SQLiteTransaction tx, long employeeId, long id, string start, string end, CancellationToken ct)
        {
            if (await c.ExecuteScalarAsync<long>(Cmd("SELECT COUNT(*) FROM EmployeeAttendance WHERE EmployeeId=@employeeId AND Id<>@id AND (@end IS NULL OR CheckInUtc<@end) AND (CheckOutUtc IS NULL OR CheckOutUtc>@start)", new { employeeId, id, start, end }, tx, ct)).ConfigureAwait(false) != 0) throw Conflict("يوجد حضور متداخل لهذا الموظف. راجع سجل الحضور.");
        }
        public async Task<Employee> EmployeeAsync(long id, CancellationToken ct)
        {
            using (var c = await OpenAsync(ct).ConfigureAwait(false)) return await c.QuerySingleOrDefaultAsync<Employee>(Cmd("SELECT * FROM Employees WHERE Id=@id", new { id }, null, ct)).ConfigureAwait(false) ?? throw Missing();
        }
        public Task<WorkforcePage<Employee>> EmployeesAsync(WorkforceQuery q, CancellationToken ct)
        {
            const string where = " WHERE (@search='' OR instr(e.Name,@search)>0 OR instr(e.JobTitle,@search)>0 OR instr(e.Phone,@search)>0 OR ('EMP-' || printf('%06d',e.Id)) LIKE @pattern) AND (@status='' OR e.IsActive=CASE WHEN @status='active' THEN 1 ELSE 0 END)";
            return PageAsync<Employee>("SELECT e.* FROM Employees e", "Employees e", where, q, null, "e.Id DESC", ct);
        }
        public Task<WorkforcePage<AttendanceRecord>> AttendanceAsync(WorkforceQuery q, CancellationToken ct)
        {
            const string from = "EmployeeAttendance a JOIN Employees e ON e.Id=a.EmployeeId";
            const string where = " WHERE (@employee IS NULL OR a.EmployeeId=@employee) AND (@search='' OR instr(e.Name,@search)>0) AND (@from IS NULL OR COALESCE(a.CheckOutUtc,'9999')>@from) AND (@to IS NULL OR a.CheckInUtc<@to) AND (@status='' OR (@status='open' AND a.CheckOutUtc IS NULL) OR (@status='closed' AND a.CheckOutUtc IS NOT NULL))";
            return PageAsync<AttendanceRecord>("SELECT a.*,e.Name EmployeeName FROM " + from, from, where, q, null, "a.CheckInUtc DESC,a.Id DESC", ct);
        }
        // Correlated sums use the covering shift index and avoid multiplying invoices by payments.
        internal const string ShiftSql = @"SELECT s.*,u.DisplayName CashierName,
            (SELECT COUNT(*) FROM CashierShifts history WHERE history.CashierId=s.CashierId) UserShiftCount,
            (SELECT COUNT(*) FROM SalesInvoiceShiftOrigins o JOIN SalesInvoices i ON i.Id=o.SalesInvoiceId WHERE o.ShiftId=s.Id AND i.RootInvoiceId IS NULL) SalesCount,
            COALESCE((SELECT SUM(AmountScaled) FROM CashierShiftEvents e WHERE e.ShiftId=s.Id AND e.Kind='cash_sale'),0) CashSalesScaled,
            COALESCE((SELECT SUM(AmountScaled) FROM CashierShiftEvents e WHERE e.ShiftId=s.Id AND e.Kind='credit_sale'),0) CreditSalesScaled,
            COALESCE((SELECT SUM(AmountScaled) FROM CashierShiftEvents e WHERE e.ShiftId=s.Id AND e.Kind='collection'),0) CollectionsScaled,
            COALESCE((SELECT SUM(AmountScaled) FROM CashierShiftEvents e WHERE e.ShiftId=s.Id AND e.Kind='in'),0) CashInScaled,
            COALESCE((SELECT SUM(AmountScaled) FROM CashierShiftEvents e WHERE e.ShiftId=s.Id AND e.Kind='out'),0) CashOutScaled
            FROM CashierShifts s JOIN Users u ON u.Id=s.CashierId";
        public Task<WorkforcePage<CashierShift>> ShiftsAsync(WorkforceQuery q, long? ownerId, CancellationToken ct)
        {
            const string where = @" WHERE (@owner IS NULL OR s.CashierId=@owner) AND (@cashier IS NULL OR s.CashierId=@cashier)
                AND (@search='' OR ('SHIFT-' || s.Id)=@search COLLATE NOCASE OR CAST(s.Id AS TEXT)=@search OR instr(u.DisplayName,@search)>0)
                AND (@from IS NULL OR s.OpenedUtc>=@from) AND (@to IS NULL OR s.OpenedUtc<@to)
                AND (@status='' OR (@status='open' AND s.ClosedUtc IS NULL) OR (@status='closed' AND s.ClosedUtc IS NOT NULL))
                AND (@discrepancies=0 OR (s.ClosedUtc IS NOT NULL AND s.CountedScaled<>s.ClosingExpectedScaled))";
            return PageAsync<CashierShift>(ShiftSql, "CashierShifts s JOIN Users u ON u.Id=s.CashierId", where, q, ownerId, "s.OpenedUtc DESC,s.Id DESC", ct);
        }
        private async Task<WorkforcePage<T>> PageAsync<T>(string select, string from, string where, WorkforceQuery q, long? ownerId, string order, CancellationToken ct)
        {
            var p = new { search = q.Search ?? "", pattern = "%" + q.Search + "%", status = q.Status ?? "", employee = q.EmployeeId, cashier = q.CashierId, from = q.FromUtc, to = q.ToUtc, owner = ownerId, discrepancies = q.DiscrepanciesOnly, limit = q.PageSize, offset = (q.Page - 1) * q.PageSize };
            using (var c = await OpenAsync(ct).ConfigureAwait(false))
            using (var tx = c.BeginTransaction(System.Data.IsolationLevel.ReadCommitted))
            {
                var total = await c.ExecuteScalarAsync<long>(Cmd("SELECT COUNT(*) FROM " + from + where, p, tx, ct)).ConfigureAwait(false);
                var rows = (await c.QueryAsync<T>(Cmd(select + where + " ORDER BY " + order + " LIMIT @limit OFFSET @offset", p, tx, ct)).ConfigureAwait(false)).ToList();
                tx.Commit(); return new WorkforcePage<T> { Items = rows, Total = total, Page = q.Page, PageSize = q.PageSize };
            }
        }
        public async Task<CashierShift> ShiftAsync(long id, long? ownerId, CancellationToken ct)
        {
            using (var c = await OpenAsync(ct).ConfigureAwait(false)) return await c.QuerySingleOrDefaultAsync<CashierShift>(Cmd(ShiftSql + " WHERE s.Id=@id AND (@ownerId IS NULL OR s.CashierId=@ownerId)", new { id, ownerId }, null, ct)).ConfigureAwait(false) ?? throw Missing();
        }
        public async Task<WorkforcePage<ShiftActivity>> ActivityAsync(long id, int page, long? ownerId, CancellationToken ct)
        {
            const string sources = @"WITH linked AS (SELECT SalesInvoiceId FROM SalesInvoiceShiftOrigins WHERE ShiftId=@id),
                resources AS (SELECT '/api/sales/' || SalesInvoiceId Resource FROM linked UNION ALL SELECT '/api/workforce/shifts/' || @id),
                paths AS (SELECT Resource FROM resources UNION SELECT r.Resource || suffix FROM resources r CROSS JOIN
                    (SELECT '/post' suffix UNION ALL SELECT '/corrections' UNION ALL SELECT '/returns' UNION ALL SELECT '/payments' UNION ALL SELECT '/cancel') suffixes),
                activity AS (
                SELECT i.Id,'invoice' Kind,i.InvoiceNumber || ' — ' || i.Status || ' — ' || (i.TotalScaled/10000) || '.' || printf('%02d',(i.TotalScaled%10000)/100) Detail,i.CreatedUtc,i.CashierUserId ActorUserId,u.DisplayName ActorDisplayName
                  FROM SalesInvoices i JOIN linked l ON l.SalesInvoiceId=i.Id LEFT JOIN Users u ON u.Id=i.CashierUserId
                UNION ALL SELECT a.Id,'audit',a.Action || ' — ' || a.Detail,a.CreatedUtc,a.ActorUserId,u.DisplayName FROM WorkforceAudit a LEFT JOIN Users u ON u.Id=a.ActorUserId WHERE a.EntityId=@id AND a.Action LIKE 'shift-%'
                UNION ALL SELECT a.Id,'invoice-audit',a.EventType || ' — ' || a.Details,a.EventUtc,a.ActorUserId,u.DisplayName FROM SalesInvoiceAudit a JOIN linked l ON l.SalesInvoiceId=a.SalesInvoiceId LEFT JOIN Users u ON u.Id=a.ActorUserId
                UNION ALL SELECT m.Id,'treasury',m.Type || ' #' || m.Id || ' — ' || (m.AmountScaled/10000) || '.' || printf('%02d',(m.AmountScaled%10000)/100),m.CreatedUtc,m.UserId,u.DisplayName FROM TreasuryMovements m LEFT JOIN Users u ON u.Id=m.UserId
                  WHERE m.Id IN (SELECT r.TreasuryMovementId FROM SalesTreasuryReceipts r JOIN linked l ON l.SalesInvoiceId=r.SalesInvoiceId
                    UNION SELECT r.MovementId FROM ReturnTreasuryLinks r JOIN SalesPayments p ON p.SalesReturnId=r.SalesReturnId JOIN CashierShiftEvents e ON e.SalesPaymentId=p.Id WHERE e.ShiftId=@id
                    UNION SELECT TreasuryMovementId FROM CashierShiftEvents WHERE ShiftId=@id AND TreasuryMovementId IS NOT NULL
                    UNION SELECT r.TreasuryMovementId FROM SalesInvoiceCorrections r JOIN linked l ON l.SalesInvoiceId=r.ReplacementInvoiceId WHERE r.TreasuryMovementId IS NOT NULL)
                UNION ALL SELECT a.Id,'operation',a.HttpMethod || ' ' || a.Resource || ' — ' || a.StatusCode,a.OccurredUtc,a.ActorUserId,a.ActorDisplayName FROM OperationAuditLog a
                  WHERE a.Resource IN (SELECT Resource FROM paths)
                ) ";
            using (var c = await OpenAsync(ct).ConfigureAwait(false))
            using (var tx = c.BeginTransaction(System.Data.IsolationLevel.ReadCommitted))
            {
                if (await c.ExecuteScalarAsync<long>(Cmd("SELECT COUNT(*) FROM CashierShifts WHERE Id=@id AND (@ownerId IS NULL OR CashierId=@ownerId)", new { id, ownerId }, tx, ct)).ConfigureAwait(false) != 1) throw Missing();
                var p = new { id, offset = (page - 1) * 50 };
                var total = await c.ExecuteScalarAsync<long>(Cmd(sources + "SELECT COUNT(*) FROM activity", p, tx, ct)).ConfigureAwait(false);
                var items = (await c.QueryAsync<ShiftActivity>(Cmd(sources + "SELECT * FROM activity ORDER BY CreatedUtc DESC,Kind,Id DESC LIMIT 50 OFFSET @offset", p, tx, ct)).ConfigureAwait(false)).ToList();
                tx.Commit(); return new WorkforcePage<ShiftActivity> { Items = items, Total = total, Page = page, PageSize = 50 };
            }
        }
        public async Task<IReadOnlyList<ShiftEvent>> EventsAsync(long id, int page, long? ownerId, CancellationToken ct)
        {
            using (var c = await OpenAsync(ct).ConfigureAwait(false)) return (await c.QueryAsync<ShiftEvent>(Cmd("SELECT e.*,u.DisplayName ActorDisplayName FROM CashierShiftEvents e JOIN CashierShifts s ON s.Id=e.ShiftId LEFT JOIN Users u ON u.Id=e.ActorUserId WHERE s.Id=@id AND (@ownerId IS NULL OR s.CashierId=@ownerId) ORDER BY e.Id DESC LIMIT 50 OFFSET @offset", new { id, ownerId, offset = (page - 1) * 50 }, null, ct)).ConfigureAwait(false)).ToList();
        }
        public async Task<IReadOnlyList<AttendanceAuditRecord>> AttendanceAuditAsync(long id, int page, CancellationToken ct)
        {
            using (var c = await OpenAsync(ct).ConfigureAwait(false)) return (await c.QueryAsync<AttendanceAuditRecord>(Cmd("SELECT Action,ActorUserId,Detail,CreatedUtc FROM WorkforceAudit WHERE EntityId=@id AND Action IN ('attendance-in','attendance-out','attendance-correct','attendance-break') ORDER BY Id DESC LIMIT 50 OFFSET @offset", new { id, offset = (page - 1) * 50 }, null, ct)).ConfigureAwait(false)).ToList();
        }
        private const string UnsettledAdvanceTotal = @"SELECT COALESCE(SUM(a.AmountScaled),0) FROM EmployeeAdvances a
            WHERE a.EmployeeId=@EmployeeId AND NOT EXISTS(SELECT 1 FROM EmployeePayrollAdvanceAllocations l WHERE l.AdvanceId=a.Id)";
        private const string PaidPeriod = "SELECT COUNT(*) FROM EmployeePayrollPayments WHERE EmployeeId=@EmployeeId AND Period=@Period";

        public Task<WorkforcePage<EmployeeAdvance>> AdvanceHistoryAsync(long? employeeId, int page, CancellationToken ct)
        {
            return Task.Run(async () =>
            {
                using (var c = await OpenAsync(ct).ConfigureAwait(false))
                using (var tx = c.BeginTransaction(System.Data.IsolationLevel.ReadCommitted))
                {
                    var result = await HistoryPage<EmployeeAdvance>(c, tx, employeeId, page,
                        @"SELECT a.Id,a.EmployeeId,e.Name EmployeeName,a.AmountScaled,a.CreatedUtc,a.Notes,a.TreasuryMovementId,
                            EXISTS(SELECT 1 FROM EmployeePayrollAdvanceAllocations l WHERE l.AdvanceId=a.Id) IsSettled",
                        " FROM EmployeeAdvances a JOIN Employees e ON e.Id=a.EmployeeId",
                        "a.EmployeeId", "a.CreatedUtc DESC,a.Id DESC", ct).ConfigureAwait(false);
                    tx.Commit();
                    return result;
                }
            }, ct);
        }

        public Task<PayrollHistory> PayrollHistoryAsync(long employeeId, string period, int page, CancellationToken ct)
        {
            return Task.Run(async () =>
            {
                using (var c = await OpenAsync(ct).ConfigureAwait(false))
                using (var tx = c.BeginTransaction(System.Data.IsolationLevel.ReadCommitted))
                {
                    var args = new { EmployeeId = employeeId, Period = period };
                    var pending = await c.ExecuteScalarAsync<long>(Cmd(UnsettledAdvanceTotal, args, tx, ct)).ConfigureAwait(false);
                    var paid = await c.ExecuteScalarAsync<long>(Cmd(PaidPeriod, args, tx, ct)).ConfigureAwait(false);
                    var history = await HistoryPage<EmployeePayroll>(c, tx, employeeId, page,
                        @"SELECT p.Id,p.EmployeeId,e.Name EmployeeName,p.SalaryScaled,p.AdvancesScaled,p.NetScaled,p.PaidUtc,p.Period,p.TreasuryMovementId",
                        " FROM EmployeePayrollPayments p JOIN Employees e ON e.Id=p.EmployeeId",
                        "p.EmployeeId", "p.PaidUtc DESC,p.Id DESC", ct).ConfigureAwait(false);
                    tx.Commit();
                    return new PayrollHistory { History = history, PendingAdvanceScaled = pending, AlreadyPaid = paid != 0 };
                }
            }, ct);
        }

        private static async Task<WorkforcePage<T>> HistoryPage<T>(SQLiteConnection c, SQLiteTransaction tx, long? employeeId,
            int page, string projection, string source, string employeeColumn, string orderBy, CancellationToken ct)
        {
            const int pageSize = 50;
            if (employeeId.HasValue) source += " WHERE " + employeeColumn + "=@EmployeeId";
            var total = await c.ExecuteScalarAsync<long>(Cmd("SELECT COUNT(*)" + source, new { EmployeeId = employeeId }, tx, ct)).ConfigureAwait(false);
            var current = (int)Math.Min(page, Math.Max(1, (total + pageSize - 1) / pageSize));
            var rows = (await c.QueryAsync<T>(Cmd(projection + source + " ORDER BY " + orderBy + " LIMIT @Limit OFFSET @Offset",
                new { EmployeeId = employeeId, Limit = pageSize, Offset = ((long)current - 1) * pageSize }, tx, ct)).ConfigureAwait(false)).AsList();
            return new WorkforcePage<T> { Items = rows, Total = total, Page = current, PageSize = pageSize };
        }

        public async Task<IReadOnlyList<EmployeeAdvance>> AdvancesAsync(long? employeeId, CancellationToken ct)
        {
            using (var c = await OpenAsync(ct).ConfigureAwait(false)) return (await c.QueryAsync<EmployeeAdvance>(Cmd("SELECT a.Id,a.EmployeeId,e.Name EmployeeName,a.AmountScaled,a.CreatedUtc,a.Notes,a.TreasuryMovementId,EXISTS(SELECT 1 FROM EmployeePayrollAdvanceAllocations l WHERE l.AdvanceId=a.Id) IsSettled FROM EmployeeAdvances a JOIN Employees e ON e.Id=a.EmployeeId WHERE (@employeeId IS NULL OR a.EmployeeId=@employeeId) ORDER BY a.CreatedUtc DESC,a.Id DESC LIMIT 500", new { employeeId }, null, ct)).ConfigureAwait(false)).ToList();
        }
        public async Task<IReadOnlyList<EmployeePayroll>> PayrollAsync(long? employeeId, CancellationToken ct)
        {
            using (var c = await OpenAsync(ct).ConfigureAwait(false)) return (await c.QueryAsync<EmployeePayroll>(Cmd("SELECT p.Id,p.EmployeeId,e.Name EmployeeName,p.SalaryScaled,p.AdvancesScaled,p.NetScaled,p.PaidUtc,p.Period,p.TreasuryMovementId FROM EmployeePayrollPayments p JOIN Employees e ON e.Id=p.EmployeeId WHERE (@employeeId IS NULL OR p.EmployeeId=@employeeId) ORDER BY p.PaidUtc DESC,p.Id DESC LIMIT 500", new { employeeId }, null, ct)).ConfigureAwait(false)).ToList();
        }
        // Called only while posting a NEW source document, inside its existing immediate writer transaction.
        public static async Task AssociateAsync(SQLiteConnection c, SQLiteTransaction tx, long actor, string kind, long amount, long? invoiceId, long? paymentId, CancellationToken ct, long? treasuryMovementId = null)
        {
            var shift = await c.QuerySingleOrDefaultAsync<CashierShift>(Cmd(ShiftSql + " WHERE s.CashierId=@actor AND s.RegisterId='standalone' AND s.ClosedUtc IS NULL", new { actor }, tx, ct)).ConfigureAwait(false);
            if (shift == null) return; // Optional lifecycle: no shift requirement for unrelated workflows.
            if (kind == "cash_sale" || kind == "collection") { var next = checked(shift.ExpectedScaled + amount); }
            else if (kind == "out") { var next = checked(shift.ExpectedScaled - amount); }
            else { var next = checked(shift.CreditSalesScaled + amount); }
            await c.ExecuteAsync(Cmd("INSERT INTO CashierShiftEvents(ShiftId,Kind,AmountScaled,SalesInvoiceId,SalesPaymentId,ActorUserId,Reason,CreatedUtc,TreasuryMovementId) VALUES(@shiftId,@kind,@amount,@invoiceId,@paymentId,@actor,'',@now,@treasuryMovementId)", new { shiftId = shift.Id, kind, amount, invoiceId, paymentId, actor, now = Now(), treasuryMovementId }, tx, ct)).ConfigureAwait(false);
        }
        private sealed class Operation { public string RequestHash { get; set; } public long EntityId { get; set; } }
    }
}
