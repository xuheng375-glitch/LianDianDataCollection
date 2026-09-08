$ErrorActionPreference='Stop'
$uiRoot=Split-Path -Parent $PSScriptRoot
$uiBin=Join-Path $uiRoot 'src\LianDian.UI\bin\Release\net472'
Add-Type -AssemblyName System.Windows.Forms
foreach($uiDll in @('SunnyUI.Common.dll','SunnyUI.dll','System.Data.SQLite.dll','LianDian.UI.exe')) { [void][Reflection.Assembly]::LoadFrom((Join-Path $uiBin $uiDll)) }
foreach($dbPath in @('Data\lian_dian.db','src\LianDian.UI\bin\Release\net472\Data\lian_dian.db','src\LianDian.UI\bin\Debug\net472\Data\lian_dian.db')) {
 $dbFull=Join-Path $uiRoot $dbPath
 if(Test-Path -LiteralPath $dbFull) {
  $dbConn=New-Object System.Data.SQLite.SQLiteConnection("Data Source=$dbFull;Read Only=True;FailIfMissing=True")
  try { $dbConn.Open();$dbCmd=$dbConn.CreateCommand();$dbCmd.CommandText='SELECT DISTINCT product_name FROM batch_record';$dbReader=$dbCmd.ExecuteReader();Write-Output $dbPath;while($dbReader.Read()){ Write-Output ('  product: '+$dbReader.GetValue(0)) };$dbReader.Dispose();$dbCmd.Dispose() } finally {$dbConn.Dispose()}
 }
}
$uiCombo=New-Object Sunny.UI.UIComboBox
$uiCombo.ShowFilter=$false
$uiCombo.DataSource=@('ALL','DH280GM')
$uiRefresh = New-Object LianDian.UI.Forms.ProductDropdownRefresh($uiCombo, [Action]{ $uiCombo.DataSource=@('ALL','DH280GM','NEW-PRODUCT'); $uiCombo.Text='ALL' })
$uiCombo.Text='ALL'
$uiForm=New-Object Sunny.UI.UIForm
$uiForm.Opacity=0
$uiForm.ShowInTaskbar=$false
$uiForm.StartPosition='Manual'
$uiForm.Location=[Drawing.Point]::new(-30000,-30000)
$uiForm.Controls.Add($uiCombo)
$uiForm.Show()
$uiFlags=[Reflection.BindingFlags]'Instance,NonPublic'
try {
 $uiMessage=[Windows.Forms.Message]::Create($uiCombo.Handle,0x201,[IntPtr]::Zero,[IntPtr]::Zero)
 [void]$uiRefresh.PreFilterMessage([ref]$uiMessage)
 $uiCombo.ShowDropDown()
 $uiDrop=[Sunny.UI.UIComboBox].GetField('dropForm',$uiFlags).GetValue($uiCombo)
 $uiList=$uiDrop.GetType().GetField('listBox',$uiFlags).GetValue($uiDrop)
 Write-Output ('Visible dropdown items: '+($uiList.Items -join ', '))
 if ($uiList.Items.Count -ne 3 -or -not $uiList.Items.Contains('DH280GM') -or -not $uiList.Items.Contains('NEW-PRODUCT')) {throw 'Dropdown refresh failed'}
 foreach($field in @('dropForm','filterForm','filterList')) {
  $uiValue=[Sunny.UI.UIComboBox].GetField($field,$uiFlags).GetValue($uiCombo)
  if($null -ne $uiValue){ Write-Output ($field+': '+$uiValue.GetType().FullName); if($field -eq 'filterList'){Write-Output $uiValue} else { $uiValue.GetType().GetFields($uiFlags) | Where-Object {$_.Name -match 'list|item'} | ForEach-Object {Write-Output ($_.Name+': '+$_.GetValue($uiValue))} } }
 }
} finally {$uiCombo.HideDropDown();$uiForm.Dispose()}
