CREATE TABLE OperationAuditLog (
    Id INTEGER PRIMARY KEY AUTOINCREMENT,
    ActorUserId INTEGER NULL,
    ActorUsername TEXT NOT NULL,
    ActorDisplayName TEXT NOT NULL,
    HttpMethod TEXT NOT NULL CHECK(HttpMethod IN ('GET','POST','PUT','PATCH','DELETE')),
    Resource TEXT NOT NULL,
    StatusCode INTEGER NOT NULL CHECK(StatusCode BETWEEN 100 AND 599),
    Outcome TEXT NOT NULL CHECK(Outcome IN ('success','failure')),
    OccurredUtc TEXT NOT NULL
);

CREATE INDEX IX_OperationAuditLog_OccurredUtc ON OperationAuditLog(OccurredUtc DESC,Id DESC);
CREATE INDEX IX_OperationAuditLog_Actor_OccurredUtc ON OperationAuditLog(ActorUserId,OccurredUtc DESC,Id DESC);
CREATE INDEX IX_OperationAuditLog_Outcome_OccurredUtc ON OperationAuditLog(Outcome,OccurredUtc DESC,Id DESC);

CREATE TRIGGER TR_OperationAuditLog_NoUpdate BEFORE UPDATE ON OperationAuditLog BEGIN SELECT RAISE(ABORT,'operation_audit_immutable'); END;
CREATE TRIGGER TR_OperationAuditLog_NoDelete BEFORE DELETE ON OperationAuditLog BEGIN SELECT RAISE(ABORT,'operation_audit_immutable'); END;
