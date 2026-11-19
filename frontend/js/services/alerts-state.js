(function (global) {
  "use strict";
  var stock = [], pending = null, dirty = true, timer = null, revision = 0, error = null;
  var types = {"low-stock":"نقص المخزون",reorder:"إعادة الطلب",expiry:"الصلاحية",installment:"الأقساط",other:"أخرى"};
  function allowed() { return global.AlKhayalAccess.routeEnabled({route:"alerts-list"}); }
  function rows() { return allowed() ? stock : []; }
  function notify() { global.dispatchEvent(new CustomEvent("alkhayal:alerts-changed")); }
  function load() {
    if (!allowed()) { stock=[];error=null;return Promise.resolve([]); }
    if (pending) return pending;
    if (!dirty) return Promise.resolve(rows());
    var started=revision; dirty=false;
    pending=global.AlKhayalApi.request("/api/alerts").then(function(result){
      if(started!==revision || !allowed())return;
      stock=result;error=null;
    }).catch(function(reason){if(started===revision){error=reason;dirty=true;}}).then(function(){
      pending=null;notify();
      if(started!==revision && allowed())return load();
      return rows();
    });
    return pending;
  }
  function invalidate() { dirty=true;revision+=1;if(timer!==null)global.clearTimeout(timer);notify();timer=global.setTimeout(function(){timer=null;load();},0); }
  function mark(id,value) {
    return global.AlKhayalApi.request("/api/alerts/"+encodeURIComponent(id)+"/read",{method:"POST",body:JSON.stringify({read:value})}).then(function(){invalidate();return load();}).catch(function(reason){error=reason;notify();return rows();});
  }
  function affectsStock(path) {
    return /^\/api\/catalog\/products(?:\/|$|\?)/.test(path) ||
      /^\/api\/inventory\/(?:post|counts\/\d+\/post|documents\/\d+\/reverse|integrity\/rebuild)(?:$|\?)/.test(path) ||
      /^\/api\/(?:sales|purchases)\/\d+\/(?:post|returns|cancel|corrections|installments)(?:$|\/|\?)/.test(path) ||
      /^\/api\/customers\/\d+\/installments(?:$|\/|\?)/.test(path) ||
      /^\/api\/returns(?:$|\?)/.test(path);
  }
  global.addEventListener("alkhayal:data-changed",function(event){if(affectsStock(event.detail.path))invalidate();});
  global.addEventListener("alkhayal:access-changed",function(){stock=[];error=null;invalidate();notify();});
  global.addEventListener("alkhayal:features-changed",function(){invalidate();});
  global.addEventListener("focus",function(){invalidate();});
  function markAll(){return global.AlKhayalApi.request("/api/alerts/read-all",{method:"POST"}).then(function(){invalidate();return load();}).catch(function(reason){error=reason;notify();return rows();});}
  global.AlKhayalAlerts={load:load,invalidate:invalidate,rows:rows,types:types,error:function(){return error;},loading:function(){return !!pending;},mark:mark,
    count:function(){return rows().filter(function(row){return !row.read;}).length;},markAll:markAll};
}(window));
