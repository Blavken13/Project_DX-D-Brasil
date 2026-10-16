param([string]$Client,[string]$Output)
$ErrorActionPreference='Stop'
$taskFramework='C:/Windows/Microsoft.NET/Framework64/v4.0.30319'
$taskRefs=@('System.dll','System.Core.dll','System.Security.dll','System.Web.Extensions.dll','System.Xaml.dll','System.Xml.dll') | ForEach-Object { Join-Path $taskFramework $_ }
$taskRefs+=@('WindowsBase.dll','PresentationCore.dll','PresentationFramework.dll') | ForEach-Object { Join-Path ($taskFramework+'/WPF') $_ }
Add-Type -AssemblyName PresentationFramework
$taskTypes=Add-Type -Path @((Join-Path $Client 'Launcher/LostHorizonLauncher.cs'),(Join-Path $Client 'Launcher/LauncherAuth.cs')) -ReferencedAssemblies $taskRefs -PassThru
$taskType=$taskTypes | Where-Object { $_.Name -eq 'LoginWindow' }
$taskFile=[IO.File]::OpenRead((Join-Path $Client 'Launcher/login.xaml'))
try { $taskWindow=[Windows.Markup.XamlReader]::Load($taskFile) } finally { $taskFile.Dispose() }
$taskCtor=$taskType.GetConstructors([Reflection.BindingFlags]'Instance,NonPublic')[0]
$taskController=$taskCtor.Invoke(@($taskWindow,$Client,$true))
$taskRoot=$taskWindow.Content
foreach($taskMode in @('login','register')) {
 if($taskMode -eq 'register') { $taskType.GetMethod('SetRegister',[Reflection.BindingFlags]'Instance,NonPublic').Invoke($taskController,@($true)) | Out-Null }
 $taskRoot.Measure((New-Object Windows.Size(1280,800)))
 $taskRoot.Arrange((New-Object Windows.Rect(0,0,1280,800)))
 $taskRoot.UpdateLayout()
 $taskBitmap=New-Object Windows.Media.Imaging.RenderTargetBitmap(1280,800,96,96,([Windows.Media.PixelFormats]::Pbgra32))
 $taskBitmap.Render($taskRoot)
 $taskEncoder=New-Object Windows.Media.Imaging.PngBitmapEncoder
 $taskEncoder.Frames.Add([Windows.Media.Imaging.BitmapFrame]::Create($taskBitmap))
 $taskStream=[IO.File]::Create((Join-Path $Output ($taskMode+'.png')))
 try { $taskEncoder.Save($taskStream) } finally { $taskStream.Dispose() }
}
$taskWindow.Close()
Write-Output 'PASS: login and registration layouts rendered without authentication'
