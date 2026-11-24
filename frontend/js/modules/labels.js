(function (global) {
  'use strict';
  var Api=global.AlKhayalApi,UI=global.AlKhayalUI,U=global.AlKhayalUtils;
  function esc(v){return U.escape(v==null?'':String(v));}
  function key(){var a=new Uint8Array(16);global.crypto.getRandomValues(a);return Array.from(a).map(function(n){return n.toString(16).padStart(2,'0');}).join('');}
  function active(c){return !c.isActive||c.isActive();}
  function option(v,n,s){return '<option value="'+esc(v)+'"'+(v===s?' selected':'')+'>'+esc(n)+'</option>';}
  function copy(code,input){if(global.navigator&&global.navigator.clipboard)global.navigator.clipboard.writeText(code).then(function(){UI.toast('تم نسخ الباركود.','success');}).catch(function(){if(input){input.focus();input.select();}UI.toast('حدد الرمز واضغط Ctrl+C لنسخه.','warning');});else{if(input){input.focus();input.select();}UI.toast('حدد الرمز واضغط Ctrl+C لنسخه.','warning');}}
  function rows(detail){var result=[];(detail.variants||[]).filter(function(v){return v.isActive&&!v.isArchived;}).forEach(function(v){(v.barcodes||[]).filter(function(b){return !b.isRetired;}).forEach(function(b){var unit=(detail.units||[]).find(function(u){return u.id===b.productUnitId;});if(unit&&unit.isActive&&unit.isSaleUnit)result.push({barcodeId:b.id,code:b.code,format:b.format||'code128',name:detail.name+' — '+(v.isDefault?'المنتج':v.name)+' / '+unit.name,variantId:v.id,productUnitId:unit.id});});});return result;}
  async function loadProductPreview(item,image,current){
    var result=await Api.request('/api/labels/product-preview',{method:'POST',body:JSON.stringify({barcodeId:item.barcodeId,format:item.format})});
    if(!current())return null;
    if(!result.pngBase64||!result.profileId||!result.templateId||!Number.isFinite(result.pageWidthMm)||!Number.isFinite(result.pageHeightMm)||result.pageWidthMm<=0||result.pageHeightMm<=0){
      throw new Error('تعذر عرض المعاينة. تحقق من إعدادات طابعة الملصقات ومقاس الورق.');
    }
    image.src='data:image/png;base64,'+result.pngBase64;
    try{await image.decode();}catch(error){if(current())throw new Error('تعذر تحميل صورة معاينة الملصق. أعد فتح المعاينة.');}
    return current()?result:null;
  }
  function previewPaper(result){
    return 'الطابعة: '+(result.printerName||'غير معيّنة')+' · مقاس الورق المحفوظ: \u2066'+result.pageWidthMm+' × '+result.pageHeightMm+'\u2069 مم';
  }
  function previewStatus(result){
    return result.printerConfigured?'معاينة الملصق على الصفحة الأولى':(result.printerMessage||'تعذر التحقق من طابعة الملصقات.');
  }
  function barcodeManager(id,afterSave,printFirst){
    var detail,busy=false,pending=null,layer,deletePending=false,previewToken=0,previewItem=null,previewResult=null;
    UI.modal({title:'باركود المنتج والمتغيرات',wide:true,body:'<div data-barcode-manager>جارٍ تحميل المنتج…</div>',actions:'<button class="button" data-modal-close>إغلاق</button>',beforeClose:function(){return !busy;},onOpen:function(m){layer=m;load();}});
    function load(){Api.request('/api/catalog/products/'+id).then(function(d){if(!layer.isConnected)return;detail=d;draw();if(printFirst){printFirst=false;var codes=rows(detail);if(codes.length)openPreview(codes[0]);else layer.querySelector('[data-barcode-error]').textContent='لا يوجد باركود محفوظ لهذا المنتج. احفظ باركودًا من هذه النافذة أولًا.';}}).catch(function(e){if(layer.isConnected)layer.querySelector('[data-barcode-manager]').innerHTML='<p role="alert">'+esc(e.message)+'</p>';});}
    function draw(){var host=layer.querySelector('[data-barcode-manager]'),codes=rows(detail);host.innerHTML='<h3>'+esc(detail.name)+'</h3><p>الاستبدال يتقاعد معه الرمز السابق ولا يغير هوية المنتج أو المستندات. Code 128 الداخلي ليس رقم GS1 رسميًا.</p><div class="table-wrap"><table class="data-table"><thead><tr><th>الصنف / المتغير / الوحدة</th><th>الرمز</th><th>النوع</th><th>الإجراءات</th></tr></thead><tbody>'+codes.map(function(b){return '<tr><td>'+esc(b.name)+'</td><td><input class="input ltr" readonly data-copy-input="'+b.barcodeId+'" value="'+esc(b.code)+'"></td><td>'+esc(b.format)+'</td><td><button class="button" data-copy="'+b.barcodeId+'">نسخ</button><button class="button" data-barcode-edit="'+b.barcodeId+'">تعديل</button><button class="button" data-barcode-print="'+b.barcodeId+'">طباعة</button><button class="button danger" data-barcode-delete="'+b.barcodeId+'">حذف</button></td></tr>';}).join('')+'</tbody></table></div><div class="form-grid"><label class="field"><span>المتغير</span><select class="select" data-variant>'+detail.variants.filter(function(v){return v.isActive&&!v.isArchived;}).map(function(v){return option(String(v.id),v.isDefault?'المنتج العادي':v.name,'');}).join('')+'</select></label><label class="field"><span>وحدة البيع</span><select class="select" data-unit>'+detail.units.filter(function(u){return u.isActive&&u.isSaleUnit;}).map(function(u){return option(String(u.id),u.name,'');}).join('')+'</select></label><label class="field"><span>الصيغة</span><select class="select" data-format><option value="code128">Code 128 — داخلي / ASCII</option><option value="ean13">EAN-13 — 12 أو 13 رقمًا</option></select></label><label class="field"><span>الباركود</span><input class="input ltr" data-code maxlength="64" autocomplete="off"></label></div><p data-replacing class="muted">إضافة رمز جديد؛ لن يُحذف أي رمز محفوظ.</p><p role="alert" data-barcode-error></p><div class="printing-actions"><button class="button primary" data-save-code>حفظ الباركود</button><button class="button" data-generate-code>توليد وحفظ Code 128 داخلي</button><button class="button" data-code-new>إضافة رمز آخر</button></div><div class="barcode-dialog" data-barcode-delete-confirm hidden role="alertdialog" aria-modal="true"><h3>تأكيد حذف الباركود</h3><p data-delete-message></p><button class="button danger" data-delete-confirm>تأكيد الحذف</button><button class="button" data-delete-cancel>إلغاء</button></div><div class="barcode-dialog" data-barcode-preview-dialog hidden role="dialog" aria-modal="true"><h3>معاينة الملصق</h3><p data-preview-caption></p><label class="field">عدد الملصقات<input class="input" data-preview-quantity type="number" min="1" max="1000" value="1"></label><p role="alert" data-preview-error></p><img data-preview-image alt="معاينة ملصق الباركود" hidden><div class="printing-actions"><button class="button primary" data-preview-print disabled>طباعة</button><button class="button" data-preview-cancel>إلغاء</button></div></div>';
      var replacement=null;
      host.onkeydown=function(event){if(event.key==='Escape'&&previewItem&&!busy){event.preventDefault();event.stopPropagation();closePreview();}};
      host.onclick=function(event){var b=event.target.closest('button');if(!b||busy)return;
        if(previewItem){
          if(b.hasAttribute('data-preview-cancel'))closePreview();
          if(b.hasAttribute('data-preview-print'))submitPreviewPrint();
          return;
        }
        if(b.hasAttribute('data-copy')){var found=codes.find(function(x){return x.barcodeId===Number(b.getAttribute('data-copy'));});copy(found.code,host.querySelector('[data-copy-input="'+found.barcodeId+'"]'));return;}
        if(b.hasAttribute('data-barcode-edit')){var item=codes.find(function(x){return x.barcodeId===Number(b.getAttribute('data-barcode-edit'));});replacement=item.barcodeId;host.querySelector('[data-variant]').value=String(item.variantId);host.querySelector('[data-unit]').value=String(item.productUnitId);host.querySelector('[data-format]').value=item.format;host.querySelector('[data-code]').value=item.code;host.querySelector('[data-replacing]').textContent='استبدال الرمز '+item.code+' — سيظل محفوظًا كرمز متقاعد.';return;}
        if(b.hasAttribute('data-barcode-print')){var printable=codes.find(function(x){return x.barcodeId===Number(b.getAttribute('data-barcode-print'));});if(printable)openPreview(printable);return;}
        if(b.hasAttribute('data-barcode-delete')){var deletable=codes.find(function(x){return x.barcodeId===Number(b.getAttribute('data-barcode-delete'));});if(deletable)openDeleteConfirmation(deletable);return;}
        if(b.hasAttribute('data-delete-cancel')){closeDeleteConfirmation();return;}
        if(b.hasAttribute('data-delete-confirm')){confirmDelete();return;}
        if(b.hasAttribute('data-code-new')){pending=null;replacement=null;host.querySelector('[data-code]').value='';host.querySelector('[data-replacing]').textContent='إضافة رمز جديد.';lock(false);return;}
        if(b.hasAttribute('data-save-code')||b.hasAttribute('data-generate-code')){var generated=b.hasAttribute('data-generate-code');if(!pending){if(generated&&host.querySelector('[data-code]').value.trim()){host.querySelector('[data-barcode-error]').textContent='امسح الرمز المكتوب أولًا إذا أردت توليد رمز آخر؛ لن نستبدله تلقائيًا.';return;}pending={productId:id,variantId:Number(host.querySelector('[data-variant]').value),productUnitId:Number(host.querySelector('[data-unit]').value),replaceBarcodeId:replacement,version:detail.version,code:generated?null:host.querySelector('[data-code]').value,format:generated?'code128':host.querySelector('[data-format]').value,generate:generated,operationKey:key()};}busy=true;lock(true);Api.request('/api/labels/barcodes',{method:'POST',body:JSON.stringify(pending)}).then(function(result){busy=false;pending=null;UI.toast('تم حفظ الباركود '+result.code,'success');if(afterSave)afterSave();if(layer.isConnected)load();}).catch(function(e){busy=false;if(e.status&&e.status<500)pending=null;if(layer.isConnected){host.querySelector('[data-barcode-error]').textContent=e.message+(pending?' — أعد المحاولة بنفس البيانات للتحقق من النتيجة.':'');lock(!!pending);host.querySelector('[data-save-code]').disabled=false;}});}
      };
      function lock(value){host.querySelectorAll('input,select,button').forEach(function(n){n.disabled=value;});if(!busy)host.querySelector('[data-code-new]').disabled=false;}
      function openDeleteConfirmation(item){if(deletePending||previewItem)return;deletePending=item;var dialog=host.querySelector('[data-barcode-delete-confirm]');dialog.querySelector('[data-delete-message]').textContent='سيتم حذف ارتباط الباركود '+item.code+' فقط. لن يتأثر المنتج أو المخزون أو الفواتير السابقة.';dialog.hidden=false;dialog.querySelector('[data-delete-confirm]').focus();}
      function closeDeleteConfirmation(){deletePending=null;host.querySelector('[data-barcode-delete-confirm]').hidden=true;}
      function confirmDelete(){if(!deletePending||busy)return;var item=deletePending,request={productId:id,barcodeId:item.barcodeId,version:detail.version,operationKey:key()};busy=true;lock(true);host.querySelector('[data-barcode-delete-confirm]').hidden=true;Api.request('/api/labels/barcodes/delete',{method:'POST',body:JSON.stringify(request)}).then(function(){deletePending=null;busy=false;UI.toast('تم حذف الباركود '+item.code,'success');if(afterSave)afterSave();if(layer.isConnected)load();}).catch(function(e){busy=false;deletePending=null;if(layer.isConnected){host.querySelector('[data-barcode-error]').textContent=e.message;lock(false);}});}
      async function openPreview(item){
        if(previewItem||deletePending)return;
        previewItem=item;
        previewResult=null;
        var token=++previewToken,dialog=host.querySelector('[data-barcode-preview-dialog]'),
          image=dialog.querySelector('[data-preview-image]'),print=dialog.querySelector('[data-preview-print]'),
          caption=dialog.querySelector('[data-preview-caption]'),status=dialog.querySelector('[data-preview-error]'),
          quantity=dialog.querySelector('[data-preview-quantity]');
        dialog.hidden=false;
        caption.textContent=item.name+' — '+item.code;
        status.textContent='جارٍ تجهيز المعاينة…';
        image.hidden=true;
        print.disabled=true;
        quantity.value='1';
        quantity.focus();
        function current(){return token===previewToken&&layer.isConnected;}
        try{
          var result=await loadProductPreview(item,image,current);
          if(!result||!current())return;
          previewResult=result;
          caption.textContent=item.name+' — '+item.code+' — '+previewPaper(result);
          status.textContent=previewStatus(result);
          image.hidden=false;
          print.disabled=!result.printerConfigured;
        }catch(error){
          if(current())status.textContent=UI.displayError(error.message);
        }
      }
      function closePreview(){
        var trigger=host.querySelector('[data-barcode-print="'+previewItem.barcodeId+'"]');
        ++previewToken;
        previewItem=null;
        previewResult=null;
        host.querySelector('[data-barcode-preview-dialog]').hidden=true;
        if(trigger)trigger.focus();
      }
      function submitPreviewPrint(){if(!previewResult||busy||host.querySelector('[data-preview-print]').disabled)return;var item=previewItem,quantity=Number(host.querySelector('[data-preview-quantity]').value);if(!Number.isInteger(quantity)||quantity<1||quantity>1000){host.querySelector('[data-preview-error]').textContent='أدخل عدد ملصقات صحيحًا بين 1 و1000.';return;}busy=true;var print=host.querySelector('[data-preview-print]');print.disabled=true;Api.request('/api/labels/jobs',{method:'POST',body:JSON.stringify({operationKey:key(),profileId:previewResult.profileId,templateId:previewResult.templateId,startCell:0,reprint:false,exactQuantity:true,items:[{barcodeId:item.barcodeId,quantity:quantity,format:item.format}]})}).then(function(job){if(job.status!=='submitted_unknown'){host.querySelector('[data-preview-error]').textContent=job.message||'تعذر تأكيد إرسال مهمة الملصق إلى Windows.';print.disabled=job.status==='uncertain';return;}UI.toast(job.message,'success');closePreview();}).catch(function(e){host.querySelector('[data-preview-error]').textContent=e.message;print.disabled=false;}).finally(function(){busy=false;});}
    }
  }
  async function sendBatch(state,button){
    if(state.busy)return;state.busy=true;if(button){button.disabled=true;button.setAttribute('aria-busy','true');}
    if(!state.pending)state.pending={operationKey:key(),profileId:state.profileId||null,templateId:state.templateId||null,startCell:Number(state.startCell)||0,reprint:!!state.lastJob,exactQuantity:!!state.exactQuantity,items:state.items.map(function(i){return {barcodeId:i.barcodeId,quantity:Number(i.quantity),format:i.format};})};
    try{var job=await Api.request('/api/labels/jobs',{method:'POST',body:JSON.stringify(state.pending)});state.pending=null;if(job.status==='submitted_unknown')state.lastJob=job;if(job.status==='submitted_unknown')UI.toast(job.message,'success');else UI.toast(job.message||'تعذر إرسال مهمة الملصقات إلى Windows.','warning');return job;}
    catch(e){if(e.status&&e.status<500)state.pending=null;UI.toast(e.message+(state.pending?' — النتيجة غير مؤكدة؛ أعد المحاولة بنفس الدفعة.':''),'warning');return null;}
    finally{state.busy=false;if(button){button.disabled=false;button.removeAttribute('aria-busy');}}
  }
  // Inline within an existing product editor: never replace its modal or read unsaved form values.
  function mountProductPrint(host,detail){
    if(global.AlKhayalFeatures&&!global.AlKhayalFeatures.isEnabled("barcode-label-printing"))return;
    var target=global.document.createElement('details');target.className='label-product-action';target.innerHTML='<summary>طباعة باركود / ملصقات محفوظة</summary><p>الطباعة تستخدم بيانات المنتج المحفوظة؛ التعديلات الحالية لن تظهر قبل الحفظ.</p><div data-inline-label></div>';host.appendChild(target);
    var loaded=false;target.ontoggle=function(){if(!target.open||loaded)return;loaded=true;Api.request('/api/labels/options').then(function(options){if(!target.isConnected)return;var codes=rows(detail),area=target.querySelector('[data-inline-label]');if(!codes.length){area.innerHTML='<p>احفظ باركودًا للمنتج أو المتغير أولًا.</p>';return;}area.innerHTML='<label class="field"><span>الباركود / الوحدة</span><select class="select" data-code-choice>'+codes.map(function(x){return option(String(x.barcodeId),x.name+' — '+x.code,'');}).join('')+'</select></label><div class="form-grid">'+batchChoices(options)+'<label class="field"><span>عدد الملصقات</span><input class="input" type="number" value="1" min="1" max="1000" data-label-quantity></label></div><button class="button primary" data-print-single>طباعة / إعادة طباعة</button>';var state={items:[]};area.querySelector('[data-print-single]').onclick=async function(){if(state.busy)return;if(!state.pending){var code=codes.find(function(x){return x.barcodeId===Number(area.querySelector('[data-code-choice]').value);});state.items=[{barcodeId:code.barcodeId,quantity:Number(area.querySelector('[data-label-quantity]').value),format:code.format}];state.profileId=null;state.templateId=null;state.exactQuantity=true;}area.querySelectorAll('input,select').forEach(function(n){n.disabled=true;});await sendBatch(state,this);if(!state.pending)area.querySelectorAll('input,select').forEach(function(n){n.disabled=false;});};}).catch(function(e){loaded=false;target.querySelector('[data-inline-label]').textContent=e.message;});};
  }
  function productPrintDialog(id){
    var state={items:[],startCell:0},closed=false;
    UI.modal({title:'طباعة باركود',body:'<div data-product-print>جارٍ تحميل المنتج والطابعة…</div>',actions:'<button class="button primary" type="button" data-direct-print disabled>طباعة</button><button class="button" data-modal-close>إغلاق</button>',beforeClose:function(){if(state.busy)return false;closed=true;return true;},onOpen:function(layer){
      var area=layer.querySelector('[data-product-print]');
      Api.request('/api/catalog/products/'+id).then(function(detail){
        if(closed||!area.isConnected)return;
        var items=rows(detail);
        if(!items.length){area.innerHTML='<p role="alert">لا يوجد باركود محفوظ صالح للبيع لهذا المنتج. احفظ باركودًا من إدارة باركود المنتج أولًا.</p>';return;}
        area.innerHTML='<h3>'+esc(detail.name)+'</h3>'+(items.length>1?'<label class="field"><span>الباركود / الوحدة</span><select class="select" data-direct-barcode>'+items.map(function(x){return option(String(x.barcodeId),x.name+' — '+x.code,'');}).join('')+'</select></label>':'<p>'+esc(items[0].name)+' — <bdi dir="ltr">'+esc(items[0].code)+'</bdi></p>')+
          '<label class="field"><span>عدد الملصقات</span><input class="input" type="number" min="1" max="1000" value="1" data-direct-quantity></label><p data-direct-paper></p><p data-direct-price></p><div class="label-preview-sheet"><img data-direct-image alt="معاينة صفحة الملصق الأول" hidden></div><p role="status" data-direct-error></p>';
        var choice=area.querySelector('[data-direct-barcode]'),quantity=area.querySelector('[data-direct-quantity]'),
          paper=area.querySelector('[data-direct-paper]'),price=area.querySelector('[data-direct-price]'),
          image=area.querySelector('[data-direct-image]'),status=area.querySelector('[data-direct-error]'),
          print=layer.querySelector('[data-direct-print]'),selected,preview,sequence=0;
        print.disabled=true;
        async function updatePreview(){
          var token=++sequence;
          selected=choice?items.find(function(x){return x.barcodeId===Number(choice.value);}):items[0];
          preview=null;
          print.disabled=true;
          image.hidden=true;
          paper.textContent='';
          price.textContent='';
          status.textContent='جارٍ تجهيز معاينة الملصق…';
          function current(){return !closed&&area.isConnected&&token===sequence;}
          try{
            var result=await loadProductPreview(selected,image,current);
            if(!result||!current())return;
            image.hidden=false;
            paper.textContent=previewPaper(result);
            price.textContent='سعر البيع المحفوظ: '+(result.price||'—')+' · الوحدة: '+(result.unit||'—');
            status.textContent=previewStatus(result);
            preview=result;
            print.disabled=!result.printerConfigured;
          }catch(error){
            if(current())status.textContent=UI.displayError(error.message);
          }
        }
        if(choice)choice.onchange=updatePreview;
        print.onclick=async function(){
          if(state.busy||!preview||!preview.printerConfigured)return;
          var count=Number(quantity.value);
          if(!Number.isInteger(count)||count<1||count>1000){
            status.textContent='أدخل عددًا صحيحًا بين 1 و1000.';
            quantity.focus();
            return;
          }
          if(!state.pending){
            state.items=[{barcodeId:selected.barcodeId,quantity:count,format:selected.format}];
            state.profileId=preview.profileId;
            state.templateId=preview.templateId;
            state.exactQuantity=true;
          }
          if(choice)choice.disabled=true;
          quantity.disabled=true;
          var job=await sendBatch(state,print);
          if(job&&(job.status==='submitted_unknown'||job.status==='uncertain')){
            status.textContent=job.message||'تعذر تأكيد خروج الملصق من الطابعة.';
            print.disabled=true;
            return;
          }
          status.textContent=state.pending?'النتيجة غير مؤكدة؛ إعادة المحاولة تُرسل الطلب نفسه.':(job&&job.message||'لم تُقبل مهمة الطباعة.');
          if(choice)choice.disabled=!!state.pending;
          quantity.disabled=!!state.pending;
        };
        updatePreview();
      }).catch(function(error){if(!closed&&area.isConnected)area.textContent=UI.displayError(error.message);});
    }});
  }
  function batchChoices(options){var p=(options.profiles||[]).find(function(x){return x.id===options.defaultProfileId;});var t=p&&(options.templates||[]).find(function(x){return x.id===p.templateId;});var stock=p&&(p.paper||(t&&t.stock));return '<p>الطابعة: '+esc(p&&p.printerName||'غير معيّنة')+' · مقاس الاستيكر: '+esc(stock?stock.pageWidthMm+' × '+stock.pageHeightMm+' مم':'غير محدد')+'</p>'; }
  function batchScreen(screen,container,context){
    var state=context.state.labels||(context.state.labels={items:[],query:'',page:1}),sequence=0,selectedProductSequence=0,optionsReady=false;
    container.innerHTML=UI.pageHeader(screen)+'<section class="card"><div class="card-body"><p>اختر أكواد المنتجات والمتغيرات المحفوظة. حد الدفعة 200 باركود و2000 ملصق شامل نسخ الطابعة.</p><div data-batch-options>جارٍ تحميل الإعدادات…</div><label class="field"><span>بحث المنتجات بالاسم / SKU / الباركود</span><input class="input" data-label-search type="search" value="'+esc(state.query)+'"></label><div data-label-products></div><div data-label-variants></div><h2>الدفعة المختارة</h2><div data-label-batch></div><div class="printing-actions"><button class="button primary" data-print-batch>طباعة / إعادة طباعة الدفعة</button><button class="button" data-clear-batch>دفعة جديدة</button></div><p data-batch-status role="status"></p></div></section>';
    var search=container.querySelector('[data-label-search]'),products=container.querySelector('[data-label-products]'),variants=container.querySelector('[data-label-variants]'),batch=container.querySelector('[data-label-batch]');
    function lock(){container.querySelectorAll('[data-label-batch] input,[data-label-batch] select,[data-label-batch] button').forEach(function(n){n.disabled=state.busy||!!state.pending;});['[data-batch-profile]','[data-batch-template]','[data-start-cell]'].forEach(function(s){var n=container.querySelector(s);if(n)n.disabled=state.busy||!!state.pending;});container.querySelector('[data-print-batch]').disabled=state.busy||!optionsReady||!state.items.length;container.querySelector('[data-clear-batch]').disabled=state.busy;}
    function drawBatch(){batch.innerHTML='<div class="table-wrap"><table class="data-table"><thead><tr><th>المنتج / المتغير / الوحدة</th><th>الباركود</th><th>الصيغة</th><th>العدد</th><th></th></tr></thead><tbody>'+state.items.map(function(i){return '<tr><td>'+esc(i.name)+'</td><td class="ltr">'+esc(i.code)+'</td><td><select class="select" data-item-format="'+i.barcodeId+'">'+option('code128','Code 128',i.format)+option('ean13','EAN-13',i.format)+'</select></td><td><input class="input" type="number" min="1" max="1000" value="'+i.quantity+'" data-item-quantity="'+i.barcodeId+'" aria-label="عدد الملصقات"></td><td><button class="button" data-remove-label="'+i.barcodeId+'">إزالة</button></td></tr>';}).join('')+'</tbody></table></div>';lock();}
    function load(){var current=++sequence;products.innerHTML='<p>جارٍ البحث…</p>';Api.request('/api/catalog/products?isActive=true&pageSize=20&page='+state.page+'&search='+encodeURIComponent(state.query)).then(function(result){if(!active(context)||current!==sequence)return;products.innerHTML='<div class="label-product-results">'+result.items.map(function(p){return '<button class="button" data-choose-label-product="'+p.id+'">'+esc(p.name)+' — '+esc(p.primaryBarcode||'بدون باركود')+'</button>';}).join('')+'</div><div class="printing-actions"><button class="button" data-label-prev'+(state.page<=1?' disabled':'')+'>السابق</button><span>صفحة '+state.page+' — '+result.totalCount+' منتج</span><button class="button" data-label-next'+(state.page*20>=result.totalCount?' disabled':'')+'>التالي</button></div>';}).catch(function(e){if(active(context)&&current===sequence)products.innerHTML='<p role="alert">'+esc(e.message)+'</p><button class="button" data-label-retry>إعادة المحاولة</button>';});}
    var timer;search.oninput=function(){state.query=search.value;state.page=1;++sequence;clearTimeout(timer);timer=setTimeout(load,180);};
    container.onclick=async function(event){var b=event.target.closest('button');if(!b)return;
      if(b.hasAttribute('data-label-prev')){state.page--;load();return;}if(b.hasAttribute('data-label-next')){state.page++;load();return;}if(b.hasAttribute('data-label-retry')){load();return;}
      if(b.hasAttribute('data-choose-label-product')){var current=++selectedProductSequence;variants.innerHTML='<p>جارٍ تحميل أكواد المنتج…</p>';try{var d=await Api.request('/api/catalog/products/'+b.getAttribute('data-choose-label-product'));if(!active(context)||current!==selectedProductSequence)return;state.choices=rows(d);variants.innerHTML=state.choices.length?state.choices.map(function(x){return '<button class="button" data-add-label="'+x.barcodeId+'">+ '+esc(x.name)+' — '+esc(x.code)+'</button>';}).join(''):'<p>لا توجد أكواد محفوظة قابلة للبيع. أضف باركودًا من دليل المنتجات.</p>';}catch(e){if(active(context)&&current===selectedProductSequence)variants.textContent=e.message;}return;}
      if(b.hasAttribute('data-add-label')&&!state.pending&&!state.busy){var i=state.choices.find(function(x){return x.barcodeId===Number(b.getAttribute('data-add-label'));});if(state.items.some(function(x){return x.barcodeId===i.barcodeId;})){UI.toast('الباركود موجود في الدفعة؛ عدّل كميته.','warning');return;}if(state.items.length>=200){UI.toast('الحد الأقصى 200 باركود.','warning');return;}state.items.push(Object.assign({quantity:1},i));drawBatch();return;}
      if(b.hasAttribute('data-remove-label')&&!state.pending&&!state.busy){state.items=state.items.filter(function(x){return x.barcodeId!==Number(b.getAttribute('data-remove-label'));});drawBatch();return;}
      if(b.hasAttribute('data-clear-batch')&&!state.busy){state.items=[];state.pending=null;state.lastJob=null;drawBatch();return;}
      if(b.hasAttribute('data-print-batch')){if(!optionsReady||!state.items.length||state.busy)return;if(!state.pending){state.profileId=null;state.templateId=null;state.exactQuantity=true;state.startCell=0;}var sending=sendBatch(state,b);lock();await sending;if(active(context)){container.querySelector('[data-batch-status]').textContent=state.pending?'النتيجة غير مؤكدة؛ إعادة المحاولة تستخدم مفتاح الدفعة نفسه.':state.lastJob?state.lastJob.message:'';lock();}}
    };
    container.oninput=function(event){var n=event.target;if(state.pending||state.busy)return;if(n.hasAttribute('data-item-quantity'))state.items.find(function(x){return x.barcodeId===Number(n.getAttribute('data-item-quantity'));}).quantity=Number(n.value);};
    container.onchange=function(event){var n=event.target;if(state.pending||state.busy)return;if(n.hasAttribute('data-item-format'))state.items.find(function(x){return x.barcodeId===Number(n.getAttribute('data-item-format'));}).format=n.value;};
    Api.request('/api/labels/options').then(function(options){if(!active(context))return;optionsReady=true;container.querySelector('[data-batch-options]').innerHTML='<div class="form-grid">'+batchChoices(options)+'</div>';drawBatch();if(context.state.productId){var id=context.state.productId;delete context.state.productId;Api.request('/api/catalog/products/'+id).then(function(d){if(active(context)){rows(d).forEach(function(i){if(!state.items.some(function(x){return x.barcodeId===i.barcodeId;}))state.items.push(Object.assign({quantity:1},i));});drawBatch();}}).catch(function(e){UI.toast(e.message,'warning');});}}).catch(function(e){if(active(context))container.querySelector('[data-batch-options]').textContent=e.message;});drawBatch();load();
  }
  var names={productName:'اسم المنتج',company:'اسم المنشأة',productCode:'مرجع المنتج',sku:'SKU',barcode:'رسم الباركود',barcodeText:'رقم الباركود',price:'سعر البيع',unit:'الوحدة',category:'التصنيف',static:'نص مخصص',logo:'الشعار'};
  function mountDesigner(host, template, draft, changeDraft, stockOverride, version, saveDesign, isCurrent){
    var original=(template.elements||[]).filter(function(e){return e.visible||e.key==='barcode';}).sort(function(a,b){return a.y-b.y;}).map(function(e){
      return {key:e.key,visible:true,fontSize:Math.round(e.fontSize*4)/4,alignment:e.key==='productName'?'center':e.alignment,bold:e.bold,text:e.text};
    });
    if(!original.length)original=[{key:'barcode',visible:true,fontSize:8,alignment:'center'}];
    var serial=0,timer=null;
    host.innerHTML='<h3>تخصيص ملصق الباركود</h3><div class="printing-designer label-content-designer"><div><p>رتّب العناصر من أعلى الملصق إلى أسفله. يبقى رسم الباركود ظاهرًا دائمًا.</p><ol class="label-design-elements" data-design-elements></ol><div class="printing-actions printing-actions-field-row"><label class="field"><span>عنصر جديد</span><select class="select" data-design-field></select></label><button class="button" type="button" data-design-add>إضافة عنصر</button></div><div class="printing-actions"><button class="button primary" type="button" data-design-save>حفظ تصميم الملصق</button><button class="button" type="button" data-design-reset>استعادة التصميم الأصلي</button></div><p role="alert" data-design-error></p></div><aside class="printing-preview"><h4>معاينة ببيانات منتج تجريبي</h4><p role="status" data-design-status></p><img data-design-img alt="معاينة تصميم ملصق الباركود" hidden></aside></div>';
    var list=host.querySelector('[data-design-elements]'),select=host.querySelector('[data-design-field]'),add=host.querySelector('[data-design-add]'),image=host.querySelector('[data-design-img]'),status=host.querySelector('[data-design-status]'),save=host.querySelector('[data-design-save]');
    function draw(focusIndex, action){
      var elements=draft||original;
      list.innerHTML=elements.map(function(e,i){
        var mandatory=e.key==='barcode',text=!mandatory&&e.key!=='logo';
        return '<li class="label-design-element" data-design-row="'+i+'"><div class="label-design-element-head"><strong>'+names[e.key]+'</strong><div class="printing-actions">'+
          '<button class="button" type="button" data-design-action="up"'+(i===0?' disabled':'')+'>أعلى</button><button class="button" type="button" data-design-action="down"'+(i===elements.length-1?' disabled':'')+'>أسفل</button>'+
          (mandatory?'':'<label><input type="checkbox" data-design-prop="visible"'+(e.visible?' checked':'')+'> إظهار</label><button class="button" type="button" data-design-action="remove">حذف</button>')+'</div></div>'+
          (text?'<div class="form-grid"><label class="field"><span>حجم الخط (نقطة)</span><input class="input" type="number" min="5" max="36" step=".25" data-design-prop="fontSize" value="'+e.fontSize+'"></label><label class="field"><span>المحاذاة</span><select class="select" data-design-prop="alignment">'+option('right','يمين',e.alignment)+option('center','وسط',e.alignment)+option('left','يسار',e.alignment)+'</select></label></div>':'')+
          (e.key==='static'?'<label class="field"><span>النص المخصص</span><input class="input" maxlength="300" dir="auto" data-design-prop="text" value="'+esc(e.text||'')+'"></label>':'')+'</li>';
      }).join('');
      var available=Object.keys(names).filter(function(key){return (key!=='logo'||template.logoPngBase64)&&!elements.some(function(e){return e.key===key;});});
      select.innerHTML=available.map(function(key){return option(key,names[key],'');}).join('');
      select.disabled=add.disabled=!available.length;
      if(action){var moved=list.querySelector('[data-design-row="'+focusIndex+'"] [data-design-action="'+action+'"]:enabled');if(moved)moved.focus();else if(!select.disabled)select.focus();}
    }
    function editable(){if(!draft)draft=original.map(function(e){return Object.assign({},e);});return draft;}
    function changed(){changeDraft(draft);host.querySelector('[data-design-error]').textContent='';preview();}
    function preview(){
      var ticket=++serial,override=stockOverride();
      global.clearTimeout(timer);image.hidden=true;save.disabled=true;
      status.textContent='جارٍ تحديث المعاينة…';
      timer=global.setTimeout(async function(){
        if(!isCurrent()||ticket!==serial)return;
        try{
          var result=await Api.request('/api/labels/design/preview',{method:'POST',body:JSON.stringify({version:version,elements:draft,stockOverride:override})});
          if(!isCurrent()||ticket!==serial)return;
          image.src='data:image/png;base64,'+result.pngBase64;image.hidden=false;
          status.textContent=override?'معاينة المقاس المحدد. احفظ تعيين الطابعة والمقاس أولًا.':'أبعاد المعاينة تتبع مقاس الاستيكر المحفوظ.';
          save.disabled=!!override;
        }catch(error){if(isCurrent()&&ticket===serial){status.textContent=error.message;save.disabled=draft!==null||!!override;}}
      },220);
    }
    list.onclick=function(event){
      var button=event.target.closest('[data-design-action]');if(!button)return;
      var index=Number(button.closest('[data-design-row]').getAttribute('data-design-row')),action=button.getAttribute('data-design-action'),items=editable();
      var next=action==='up'?index-1:index+1;
      if(action==='remove')items.splice(index,1);
      else if(next>=0&&next<items.length){var item=items.splice(index,1)[0];items.splice(next,0,item);}
      draw(next,action);changed();
    };
    list.oninput=function(event){
      var field=event.target,prop=field.getAttribute('data-design-prop');if(!prop)return;
      var index=Number(field.closest('[data-design-row]').getAttribute('data-design-row'));
      editable()[index][prop]=field.type==='checkbox'?field.checked:field.type==='number'?Number(field.value):field.value;
      changed();
    };
    add.onclick=function(){editable().push({key:select.value,visible:true,fontSize:8,alignment:'center'});draw();changed();};
    host.querySelector('[data-design-reset]').onclick=function(){draft=null;draw();changed();};
    save.onclick=function(){saveDesign(draft,save,host.querySelector('[data-design-error]'));};
    draw();preview();
    return preview;
  }
  function mountSettings(parent,context,printers){
    var box=global.document.createElement('div');box.className='label-printer-assignment';parent.appendChild(box);
    var devices=printers||[],settings,busy=false,draft,devicePhase='loaded';
    function printerChoices(selected) {
      var missing = devicePhase === 'loaded' || devicePhase === 'empty' ? 'غير موجودة في قائمة Windows الحالية' : 'تعذّر التحقق من الطابعة حاليًا';
      return option('', 'اختر طابعة Windows', selected) + devices.map(function (p) {
        return option(p.name, p.name, selected);
      }).join('') + (selected && !devices.some(function (p) { return p.name === selected; }) ? option(selected, selected + ' — ' + missing, selected) : '');
    }
    function updateDevices(next, phase) {
      devices = next;
      devicePhase = phase;
      var select = box.querySelector('[data-simple-printer]');
      if (select) select.innerHTML = printerChoices(select.value);
    }
    function current(){return settings.profiles.find(function(p){return p.id===settings.defaultProfileId;});}
    function render(){if(context.isCurrent ? !context.isCurrent() || !parent.contains(box) : !box.isConnected)return;var profile=current();if(!profile){box.innerHTML='<p role="alert">لا يوجد ملف طابعة ملصقات افتراضي محفوظ. تحقق من إعدادات الطباعة.</p>';return;}var template=(settings.templates||[]).find(function(t){return t.id===profile.templateId;}),stock=template&&template.stock,width=stock&&Number(stock.pageWidthMm),height=stock&&Number(stock.pageHeightMm),size=width&&height?width+'x'+height:'',preset=['35x25','40x30','50x30','60x40'].indexOf(size)>=0?size:'custom';
      box.innerHTML='<label class="field"><span>تعيين طابعة ملصقات باركود</span><select class="select" data-simple-printer>'+printerChoices(profile.printerName||'')+'</select></label><label class="field"><span>مقاس الاستيكر</span><select class="select" data-simple-size>'+[['35x25','35 × 25 mm'],['40x30','40 × 30 mm'],['50x30','50 × 30 mm'],['60x40','60 × 40 mm'],['custom','مقاس مخصص']].map(function(x){return option(x[0],x[1],preset);}).join('')+'</select></label><div class="form-grid" data-custom-size'+(preset==='custom'?'':' hidden')+'><label class="field"><span>العرض (مم)</span><input class="input" type="number" min="10" max="420" step="0.1" data-simple-width value="'+esc(width||'')+'"></label><label class="field"><span>الارتفاع (مم)</span><input class="input" type="number" min="10" max="1000" step="0.1" data-simple-height value="'+esc(height||'')+'"></label></div><p class="muted">يتحقق The Cashier من نموذج ورق مطابق في تعريف Windows قبل الحفظ والطباعة.</p><button class="button primary" data-simple-save>حفظ طابعة الباركود والمقاس</button><p role="alert" data-simple-error></p>';
      var designer=global.document.createElement('section');
      designer.className='label-content-settings';box.appendChild(designer);
      var refresh=mountDesigner(designer,template,draft,function(value){draft=value;},function(){
        var selected=dimensions(),printer=box.querySelector('[data-simple-printer]').value;
        return selected[0]===width&&selected[1]===height&&printer===(profile.printerName||'')?null:{widthMm:selected[0],heightMm:selected[1],printerName:printer};
      },settings.version,saveDesign,function(){
        return context.isCurrent ? context.isCurrent() && parent.contains(box) && box.contains(designer) : designer.isConnected;
      });
      box.querySelector('[data-simple-size]').onchange=function(){box.querySelector('[data-custom-size]').hidden=this.value!=='custom';refresh();};
      ['data-simple-printer','data-simple-width','data-simple-height'].forEach(function(key){box.querySelector('['+key+']').oninput=refresh;});
      box.querySelector('[data-simple-save]').onclick=save;
    }
    function dimensions(){var value=box.querySelector('[data-simple-size]').value;return value==='custom'?[Number(box.querySelector('[data-simple-width]').value),Number(box.querySelector('[data-simple-height]').value)]:value.split('x').map(Number);}
    async function save(){if(busy)return;var button=box.querySelector('[data-simple-save]'),parts=dimensions();if(parts.length!==2||!isFinite(parts[0])||!isFinite(parts[1])||parts[0]<10||parts[0]>420||parts[1]<10||parts[1]>1000){box.querySelector('[data-simple-error]').textContent='أدخل عرضًا وارتفاعًا صالحين بالملليمتر.';return;}busy=true;button.disabled=true;try{settings=await Api.request('/api/labels/printer-assignment',{method:'PUT',body:JSON.stringify({version:settings.version,printerName:box.querySelector('[data-simple-printer]').value,widthMm:parts[0],heightMm:parts[1]})});render();UI.toast('حُفظت طابعة الباركود ومقاس الاستيكر.','success');}catch(error){box.querySelector('[data-simple-error]').textContent=error.message;button.disabled=false;}finally{busy=false;}}
    async function saveDesign(elements,button,errorNode){
      if(busy)return;
      busy=true;button.disabled=true;errorNode.textContent='';
      var controls=box.querySelectorAll('button, input, select');
      var disabled=Array.from(controls,function(control){return control.disabled;});
      controls.forEach(function(control){control.disabled=true;});
      try{
        settings=await Api.request('/api/labels/design',{method:'PUT',body:JSON.stringify({version:settings.version,elements:elements})});
        draft=settings.design;render();UI.toast('حُفظ تصميم ملصق الباركود.','success');
      }catch(error){errorNode.textContent=error.message;controls.forEach(function(control,index){control.disabled=disabled[index];});button.disabled=false;}
      finally{busy=false;}
    }
    function load() {
      box.innerHTML = '<p role="status">جارٍ تحميل إعدادات ملصقات الباركود…</p>';
      Api.request('/api/labels/settings').then(function (result) {
        settings = result; draft = settings.design || null; render();
      }).catch(function (error) {
        if (context.isCurrent && !context.isCurrent()) return;
        box.innerHTML = '<p role="alert">' + esc(error.message) + '</p><button class="button" data-retry-label-settings>إعادة المحاولة</button>';
        box.querySelector('[data-retry-label-settings]').onclick = load;
      });
    }
    load();
    return updateDevices;
  }
  global.AlKhayalLabels={barcodeManager:barcodeManager,productPrintDialog:productPrintDialog,mountProductPrint:mountProductPrint,mountSettings:mountSettings,copy:copy,sendBatch:sendBatch};
  global.AlKhayalRenderers['product-labels']=batchScreen;
}(window));
