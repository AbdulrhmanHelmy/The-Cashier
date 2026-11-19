using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace AlKhayal.UI
{
    [ComVisible(true)]
    public sealed class DesktopExportDialogs
    {
        private readonly Form owner; private string selectedPath;
        internal DesktopExportDialogs(Form owner){this.owner=owner;}
        public string pickFile(string format,string suggestedName)=>OnUiThread(()=>
        {
            var extension=string.Equals(format,"xlsx",StringComparison.OrdinalIgnoreCase)?"xlsx":"csv";
            using(var dialog=new SaveFileDialog{AddExtension=true,DefaultExt=extension,FileName=SafeName(suggestedName)+"."+extension,Filter=extension=="xlsx"?"Excel Workbook (*.xlsx)|*.xlsx":"CSV UTF-8 (*.csv)|*.csv",OverwritePrompt=true,Title="حفظ ملف التصدير"})
            {if(dialog.ShowDialog(owner)!=DialogResult.OK){selectedPath=null;return "";}selectedPath=Path.GetFullPath(dialog.FileName);return selectedPath;}
        });
        public bool saveFile(string path,string base64)=>OnUiThread(()=>
        {
            var full=Path.GetFullPath(path??"");if(string.IsNullOrEmpty(selectedPath)||!string.Equals(full,selectedPath,StringComparison.OrdinalIgnoreCase))throw new InvalidOperationException("مسار حفظ التصدير غير معتمد.");
            var directory=Path.GetDirectoryName(full);if(!Directory.Exists(directory))throw new DirectoryNotFoundException("مجلد الحفظ لم يعد متاحًا.");
            var temp=Path.Combine(directory,"."+Path.GetFileName(full)+"."+Guid.NewGuid().ToString("N")+".tmp");
            try{File.WriteAllBytes(temp,Convert.FromBase64String(base64??""));if(File.Exists(full))File.Replace(temp,full,null);else File.Move(temp,full);selectedPath=null;return true;}finally{if(File.Exists(temp))File.Delete(temp);}
        });
        private static string SafeName(string value){foreach(var c in Path.GetInvalidFileNameChars())value=(value??"export").Replace(c,'-');return string.IsNullOrWhiteSpace(value)?"export":value;}
        private T OnUiThread<T>(Func<T> action)=>owner.InvokeRequired?(T)owner.Invoke(action):action();
    }
}
