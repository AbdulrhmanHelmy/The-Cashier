(function(global){
  "use strict";
  var A=global.AlKhayalAlerts,U=global.AlKhayalUtils,UI=global.AlKhayalUI;
  global.AlKhayalAlertsPage=function(screen,container,context){
    context=context||{state:{}};
    var state=context.state,selected=state.alertType||"";
    container.innerHTML=UI.pageHeader(screen,'<button class="button" type="button" data-alert-refresh>تحديث</button><button class="button" type="button" data-mark-all>تعليم الكل كمقروء</button>')+
      '<section class="card"><div class="card-body"><div class="field"><label for="alertType">نوع التنبيه</label><select class="select" id="alertType"><option value="">الكل</option>'+Object.keys(A.types).map(function(key){return '<option value="'+key+'"'+(selected===key?' selected':'')+'>'+A.types[key]+'</option>';}).join("")+'</select></div><div data-alert-status role="status"></div><div class="list" data-alert-rows></div></div></section>';
    function draw(){
      var rows=A.rows().filter(function(row){return !state.alertType || row.type===state.alertType;});
      var error=A.error();container.querySelector("[data-alert-status]").textContent=error?"تعذر تحديث التنبيهات: "+error.message:A.loading()?"جارٍ تحميل التنبيهات…":"";
      container.querySelector("[data-alert-rows]").innerHTML=rows.length?rows.map(function(row){var link=row.relatedRoute&&context.canNavigate&&context.canNavigate(row.relatedRoute)?'<button class="button ghost" type="button" data-open-related>فتح السجل</button>':'';return '<article class="list-item" data-alert="'+U.escape(row.id)+'"><div>'+UI.badge(row.severity)+' <strong>'+U.escape(row.title)+'</strong> '+(!row.read?'<span class="badge info">جديد</span>':'')+'<p class="muted">'+U.escape(row.detail)+(row.date?' · '+U.dateTime(row.date):'')+'</p></div><div class="page-actions">'+link+'<button class="button ghost" type="button" data-toggle-read>'+(row.read?'غير مقروء':'مقروء')+'</button></div></article>';}).join(""):'<div class="empty-state"><strong>لا توجد تنبيهات مطابقة</strong><span>لا توجد سجلات محفوظة تستوفي هذا النوع حاليًا.</span></div>';
    }
    container.querySelector("#alertType").onchange=function(){state.alertType=this.value;draw();};
    container.onclick=function(event){
      if(event.target.closest("[data-mark-all]")){A.markAll();return;}
      if(event.target.closest("[data-alert-refresh]")){A.invalidate();return;}
      var item=event.target.closest("[data-alert]"),row=item&&A.rows().find(function(x){return x.id===item.getAttribute("data-alert");});
      if(row&&event.target.closest("[data-open-related]")){if(row.relatedRoute==="product-edit")context.navigate(row.relatedRoute,{editProductId:Number(row.relatedId)});else if(row.relatedRoute==="installment-payment")context.navigate(row.relatedRoute,{installmentContractId:Number(row.relatedId),installmentCustomerId:Number(row.relatedParentId)});return;}
      if(row&&event.target.closest("[data-toggle-read]")){A.mark(row.id,!row.read);}
    };
    container.refreshAlerts=draw;
    draw();
  };
}(window));
