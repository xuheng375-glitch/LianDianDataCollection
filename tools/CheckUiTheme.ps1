param([string]$BinPath)
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Windows.Forms
Add-Type -AssemblyName System.Drawing
$uiRoot = Split-Path -Parent $PSScriptRoot
$uiBin = Join-Path $uiRoot 'src\LianDian.UI\bin\Release\net472'
if ($BinPath) { $uiBin = [IO.Path]::GetFullPath($BinPath) }
foreach ($uiAssembly in @('SunnyUI.Common.dll','SunnyUI.dll','System.Data.SQLite.dll','log4net.dll','NModbus.dll','LianDian.Core.dll','LianDian.Data.dll','LianDian.Comm.dll','LianDian.Business.dll','LianDian.UI.exe')) {
    [void][Reflection.Assembly]::LoadFrom((Join-Path $uiBin $uiAssembly))
}
[Windows.Forms.Application]::EnableVisualStyles()
[Sunny.UI.UIStyles]::InitColorful([LianDian.UI.Theme.FlatTheme]::Cyan,[LianDian.UI.Theme.FlatTheme]::Text)
$uiTemp = Join-Path ([IO.Path]::GetTempPath()) ('LianDianUi-' + [Guid]::NewGuid().ToString('N'))
$uiConfig = New-Object LianDian.Core.Config.AppConfig
$uiConfig.Db.FilePath = Join-Path $uiTemp 'preview.db'
$uiConfig.Paths.LogDir = Join-Path $uiTemp 'Logs'
$uiInstructionDir = Join-Path $uiTemp 'Instruction'
[void][IO.Directory]::CreateDirectory($uiInstructionDir)
# Self-contained fixture: border and corners reveal cropping; no production assets.
$uiFixture = New-Object Drawing.Bitmap(640,480)
$uiGraphics = [Drawing.Graphics]::FromImage($uiFixture)
try {
    $uiGraphics.Clear([Drawing.Color]::FromArgb(20,80,100))
    $uiGraphics.DrawRectangle([Drawing.Pens]::Cyan,2,2,635,475)
    $uiGraphics.DrawString('PREVIEW - COMPLETE IMAGE',[Drawing.SystemFonts]::DefaultFont,[Drawing.Brushes]::White,160,230)
    $uiFixture.Save((Join-Path $uiInstructionDir 'PREVIEW-1.png'),[Drawing.Imaging.ImageFormat]::Png)
    $uiFixture.Save((Join-Path $uiInstructionDir 'PREVIEW-2.png'),[Drawing.Imaging.ImageFormat]::Png)
} finally { $uiGraphics.Dispose(); $uiFixture.Dispose() }
# Deliberately do not call SystemManager.Start: no PLC connection, heartbeat or writes.
$uiManager = [LianDian.Business.SystemManager]::Create($uiConfig)
$uiMain = New-Object LianDian.UI.MainForm($uiManager)
$uiInstructions = New-Object LianDian.UI.Forms.InstructionForm($uiInstructionDir, 'PREVIEW')
$uiExit = New-Object LianDian.UI.Forms.ExitConfirmForm
try {
    foreach ($uiForm in @($uiMain,$uiInstructions,$uiExit)) {
        $uiForm.ShowInTaskbar = $false
        $uiForm.Opacity = 0
        $uiForm.StartPosition = [Windows.Forms.FormStartPosition]::Manual
        $uiForm.Location = [Drawing.Point]::new(-30000,-30000)
        $uiForm.Show()
    }
    [Windows.Forms.Application]::DoEvents()
    if ($uiInstructions.Bounds -ne [Windows.Forms.Screen]::FromControl($uiInstructions).Bounds) { throw 'Instruction not fullscreen' }
    $uiFlags = [Reflection.BindingFlags]'NonPublic,Instance'
    $uiMain.GetType().GetField('_picTimer',$uiFlags).GetValue($uiMain).Stop()
    for ($uiWait=0;$uiWait -lt 200 -and $uiMain.GetType().GetField('_queryRunning',$uiFlags).GetValue($uiMain);$uiWait++) {
        [Windows.Forms.Application]::DoEvents()
        Start-Sleep -Milliseconds 10
    }
    $uiPages = $uiInstructions.GetType().GetField('_images',$uiFlags).GetValue($uiInstructions)
    if ($uiPages.Count -ne 2) { throw 'Instruction matching failed' }
    $uiMissing = New-Object LianDian.UI.Forms.InstructionForm((Join-Path $uiRoot 'Instruction'), 'NO-SUCH-PRODUCT')
    try {
        if ($uiMissing.GetType().GetField('_images',$uiFlags).GetValue($uiMissing).Count -ne 0) { throw 'Unrelated instruction fallback detected' }
    } finally { $uiMissing.Dispose() }
    $uiImage = $uiInstructions.GetType().GetField('_picture',$uiFlags).GetValue($uiInstructions)
    $uiViewport = $uiInstructions.GetType().GetField('_viewport',$uiFlags).GetValue($uiInstructions)
    if ($uiImage.Width -gt $uiViewport.Width -or $uiImage.Height -gt $uiViewport.Height) { throw 'Image does not fit viewport' }
    $uiConfigType = $uiMain.GetType()
    $uiCsv = $uiConfigType.GetMethod('CsvCell',[Reflection.BindingFlags]'NonPublic,Static')
    if ($uiCsv.Invoke($null,@('00005')) -ne '"00005"') { throw 'CSV identifier changed' }
    if ($uiCsv.Invoke($null,@('=1+1')) -ne '"''=1+1"') { throw 'CSV formula guard failed' }
    Write-Output 'Instruction matching, fit and CSV checks passed (no PLC started).'
    $uiGrid = $uiMain.GetType().GetField('_grid',$uiFlags).GetValue($uiMain)
    $uiLog = $uiMain.GetType().GetField('_eventLog',$uiFlags).GetValue($uiMain)
    $uiPause = $uiMain.GetType().GetField('_pauseLogButton',$uiFlags).GetValue($uiMain)
    $uiLogCount = $uiLog.Rows.Count
    $uiPause.PerformClick()
    $uiMain.GetType().GetMethod('AddImportantLog',$uiFlags).Invoke($uiMain,@('回归检查：暂停期间记录成功'))
    if ($uiLog.Rows.Count -ne $uiLogCount) { throw 'Paused log view changed' }
    $uiPause.PerformClick()
    if ($uiLog.Rows.Count -ne $uiLogCount + 1) { throw 'Paused log was lost' }
    Write-Output 'Log pause and resume passed.'
    $uiProduct = $uiMain.GetType().GetField('_productImage',$uiFlags).GetValue($uiMain)
    $uiProduct.BackgroundImage = [LianDian.UI.ImageResolver]::LoadSafely((Join-Path $uiInstructionDir 'PREVIEW-1.png'))
    $uiSavedProduct = Join-Path $uiRoot 'assets\ProductImages\DH280GM.png'
    if (Test-Path -LiteralPath $uiSavedProduct) {
        $uiProduct.BackgroundImage.Dispose()
        $uiProduct.BackgroundImage = [LianDian.UI.ImageResolver]::LoadSafely($uiSavedProduct)
    }
    [void]$uiGrid.Rows.Add([object[]]@('26250','00005','DH280GM','600249','41244.00','241124.00','2412412.00','OK','12223.00','OK','2026-09-07 21:45:08','A'))
    $uiMain.GetType().GetField('_countLabel',$uiFlags).GetValue($uiMain).Text = '1 record (preview)'
    foreach ($uiField in @('_productValue','_employeeValue','_dateValue','_batchNoLabel')) {
        $uiLabel = $uiMain.GetType().GetField($uiField,$uiFlags).GetValue($uiMain)
        $uiLabel.Text = @{_productValue='DH280GM';_employeeValue='600249';_dateValue='26250';_batchNoLabel='00006'}[$uiField]
    }
    foreach ($uiEntry in @(@($uiMain,'ui-main-preview.png'),@($uiInstructions,'ui-instruction-preview.png'),@($uiExit,'ui-exit-preview.png'))) {
        $uiForm = $uiEntry[0]
        [void]$uiForm.Handle
        $uiForm.PerformLayout()
        $uiBitmap = New-Object Drawing.Bitmap($uiForm.Width,$uiForm.Height)
        try {
            $uiForm.DrawToBitmap($uiBitmap,[Drawing.Rectangle]::new(0,0,$uiForm.Width,$uiForm.Height))
            $uiBitmap.Save((Join-Path $uiRoot ('docs\'+$uiEntry[1])),[Drawing.Imaging.ImageFormat]::Png)
        } finally { $uiBitmap.Dispose() }
    }
    Write-Output ('Grid background: '+$uiGrid.BackgroundColor+'; text: '+$uiGrid.DefaultCellStyle.ForeColor)
    foreach ($uiSize in @([Drawing.Size]::new(1920,1080),[Drawing.Size]::new(1600,900),[Drawing.Size]::new(1280,720))) {
        $uiMain.ClientSize=$uiSize
        $uiMain.PerformLayout()
        foreach($uiControl in $uiMain.Controls) {
            if($uiControl.Visible -and ($uiControl.Right -gt $uiMain.ClientSize.Width -or $uiControl.Bottom -gt $uiMain.ClientSize.Height)) {
                throw ('Top-level clipping at '+$uiSize+': '+$uiControl.GetType().Name)
            }
        }
        $uiBitmap=New-Object Drawing.Bitmap($uiSize.Width,$uiSize.Height)
        try {
            $uiMain.DrawToBitmap($uiBitmap,[Drawing.Rectangle]::new(0,0,$uiSize.Width,$uiSize.Height))
            $uiBitmap.Save((Join-Path $uiRoot ('docs\ui-layout-'+$uiSize.Width+'x'+$uiSize.Height+'.png')),[Drawing.Imaging.ImageFormat]::Png)
        } finally {$uiBitmap.Dispose()}
        Write-Output ('Layout bounds passed: '+$uiSize)
    }
} finally {
    $uiMain.Dispose()
    $uiInstructions.Dispose()
    $uiExit.Dispose()
    $uiManager.Dispose()
}
