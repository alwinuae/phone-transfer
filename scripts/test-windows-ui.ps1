param(
    [Parameter(Mandatory = $true)]
    [string]$ExePath,

    [string]$HostAddress = "127.0.0.1",

    [int]$Port = 8765,

    [Parameter(Mandatory = $true)]
    [string]$AccessCode,

    [string]$ArtifactDirectory = "artifacts\windows-ui-smoke"
)

$ErrorActionPreference = "Stop"
Add-Type -AssemblyName UIAutomationClient
Add-Type -AssemblyName UIAutomationTypes
Add-Type -AssemblyName System.Windows.Forms
Add-Type @"
using System;
using System.Runtime.InteropServices;

public static class PhoneFolderNativeUi
{
    [DllImport("user32.dll")]
    public static extern bool SetForegroundWindow(IntPtr window);

    [DllImport("user32.dll")]
    public static extern bool SetCursorPos(int x, int y);

    [DllImport("user32.dll")]
    public static extern void mouse_event(uint flags, uint dx, uint dy, uint data, UIntPtr extraInfo);

    [DllImport("user32.dll", EntryPoint = "GetClassLongPtrW")]
    public static extern IntPtr GetClassLongPtr(IntPtr window, int index);

    [DllImport("user32.dll")]
    public static extern IntPtr SendMessage(IntPtr window, uint message, IntPtr wParam, IntPtr lParam);
}
"@

$exe = (Resolve-Path -LiteralPath $ExePath).Path
$artifactRoot = [IO.Path]::GetFullPath((Join-Path (Get-Location) $ArtifactDirectory))
New-Item -ItemType Directory -Path $artifactRoot -Force | Out-Null
$downloadRoot = Join-Path $artifactRoot "downloads"
New-Item -ItemType Directory -Path $downloadRoot -Force | Out-Null
$sourcePath = Join-Path $artifactRoot "ui-upload.txt"
$sourceContents = "Phone Transfer packaged Windows UI smoke test $([DateTime]::UtcNow.ToString("O"))`n"
[IO.File]::WriteAllText($sourcePath, $sourceContents)
$previewPath = Join-Path $artifactRoot "ui-preview.png"
[IO.File]::WriteAllBytes(
    $previewPath,
    [Convert]::FromBase64String(
        "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mNk+A8AAQUBAScY42YAAAAASUVORK5CYII="))
$stagePath = Join-Path $artifactRoot "stage.txt"

function Set-TestStage {
    param([string]$Stage)

    [IO.File]::WriteAllText($stagePath, $Stage)
    Write-Host "Stage: $Stage"
}

function Get-DesktopWindow {
    param(
        [int]$ProcessId,
        [string]$Name,
        [int]$TimeoutSeconds = 10
    )

    $desktop = [System.Windows.Automation.AutomationElement]::RootElement
    $processCondition = [System.Windows.Automation.PropertyCondition]::new(
        [System.Windows.Automation.AutomationElement]::ProcessIdProperty,
        $ProcessId)
    $deadline = [DateTime]::UtcNow.AddSeconds($TimeoutSeconds)
    do {
        $windows = $desktop.FindAll(
            [System.Windows.Automation.TreeScope]::Children,
            $processCondition)
        for ($index = 0; $index -lt $windows.Count; $index++) {
            $window = $windows.Item($index)
            if ([string]::IsNullOrEmpty($Name) -or $window.Current.Name -eq $Name) {
                return $window
            }
        }

        Start-Sleep -Milliseconds 150
    } while ([DateTime]::UtcNow -lt $deadline)

    $allWindows = $desktop.FindAll(
        [System.Windows.Automation.TreeScope]::Children,
        [System.Windows.Automation.Condition]::TrueCondition)
    $processWindow = $null
    $visible = for ($index = 0; $index -lt $allWindows.Count; $index++) {
        $window = $allWindows.Item($index)
        if ($window.Current.ProcessId -eq $ProcessId) {
            if ($null -eq $processWindow) {
                $processWindow = $window
            }
            "'$($window.Current.Name)' [$($window.Current.ClassName)]"
        }
    }
    if ($null -ne $processWindow -and [string]::IsNullOrEmpty($Name)) {
        return $processWindow
    }
    throw "Window '$Name' was not found for process $ProcessId. Visible: $($visible -join ', ')."
}

function Find-Element {
    param(
        [System.Windows.Automation.AutomationElement]$Root,
        [string]$AutomationId,
        [string]$Name,
        [System.Windows.Automation.ControlType]$ControlType,
        [int]$TimeoutSeconds = 10
    )

    $conditions = [System.Collections.Generic.List[System.Windows.Automation.Condition]]::new()
    if (-not [string]::IsNullOrEmpty($AutomationId)) {
        $conditions.Add([System.Windows.Automation.PropertyCondition]::new(
            [System.Windows.Automation.AutomationElement]::AutomationIdProperty,
            $AutomationId))
    }
    if (-not [string]::IsNullOrEmpty($Name)) {
        $conditions.Add([System.Windows.Automation.PropertyCondition]::new(
            [System.Windows.Automation.AutomationElement]::NameProperty,
            $Name))
    }
    if ($null -ne $ControlType) {
        $conditions.Add([System.Windows.Automation.PropertyCondition]::new(
            [System.Windows.Automation.AutomationElement]::ControlTypeProperty,
            $ControlType))
    }
    if ($conditions.Count -eq 0) {
        throw "At least one element condition is required."
    }

    $condition = if ($conditions.Count -eq 1) {
        $conditions[0]
    } else {
        [System.Windows.Automation.AndCondition]::new($conditions.ToArray())
    }
    $deadline = [DateTime]::UtcNow.AddSeconds($TimeoutSeconds)
    do {
        $element = $Root.FindFirst(
            [System.Windows.Automation.TreeScope]::Descendants,
            $condition)
        if ($null -ne $element) {
            return $element
        }
        Start-Sleep -Milliseconds 150
    } while ([DateTime]::UtcNow -lt $deadline)

    throw "Automation element was not found. Id='$AutomationId' Name='$Name'."
}

function Find-ProcessElement {
    param(
        [int]$ProcessId,
        [string]$Name,
        [System.Windows.Automation.ControlType]$ControlType,
        [int]$TimeoutSeconds = 10
    )

    $conditions = @(
        [System.Windows.Automation.PropertyCondition]::new(
            [System.Windows.Automation.AutomationElement]::ProcessIdProperty,
            $ProcessId),
        [System.Windows.Automation.PropertyCondition]::new(
            [System.Windows.Automation.AutomationElement]::NameProperty,
            $Name),
        [System.Windows.Automation.PropertyCondition]::new(
            [System.Windows.Automation.AutomationElement]::ControlTypeProperty,
            $ControlType)
    )
    $condition = [System.Windows.Automation.AndCondition]::new($conditions)
    $desktop = [System.Windows.Automation.AutomationElement]::RootElement
    $deadline = [DateTime]::UtcNow.AddSeconds($TimeoutSeconds)
    do {
        $element = $desktop.FindFirst(
            [System.Windows.Automation.TreeScope]::Descendants,
            $condition)
        if ($null -ne $element) {
            return $element
        }
        Start-Sleep -Milliseconds 100
    } while ([DateTime]::UtcNow -lt $deadline)

    throw "Process automation element was not found. Name='$Name'."
}

function Get-ModalWindow {
    param(
        [int]$ProcessId,
        [string]$Name,
        [int]$TimeoutSeconds = 10
    )

    $desktop = [System.Windows.Automation.AutomationElement]::RootElement
    $nameCondition = [System.Windows.Automation.PropertyCondition]::new(
        [System.Windows.Automation.AutomationElement]::NameProperty,
        $Name)
    $windowCondition = [System.Windows.Automation.PropertyCondition]::new(
        [System.Windows.Automation.AutomationElement]::ControlTypeProperty,
        [System.Windows.Automation.ControlType]::Window)
    $condition = [System.Windows.Automation.AndCondition]::new(
        $nameCondition,
        $windowCondition)
    $processCondition = [System.Windows.Automation.PropertyCondition]::new(
        [System.Windows.Automation.AutomationElement]::ProcessIdProperty,
        $ProcessId)
    $deadline = [DateTime]::UtcNow.AddSeconds($TimeoutSeconds)
    do {
        $applicationWindows = $desktop.FindAll(
            [System.Windows.Automation.TreeScope]::Children,
            $processCondition)
        for ($rootIndex = 0; $rootIndex -lt $applicationWindows.Count; $rootIndex++) {
            $applicationWindow = $applicationWindows.Item($rootIndex)
            $windows = $applicationWindow.FindAll(
                [System.Windows.Automation.TreeScope]::Subtree,
                $condition)
            for ($index = 0; $index -lt $windows.Count; $index++) {
                $window = $windows.Item($index)
                $pattern = $null
                $hasWindowPattern = $window.TryGetCurrentPattern(
                    [System.Windows.Automation.WindowPattern]::Pattern,
                    [ref]$pattern)
                if ($hasWindowPattern -and $pattern.Current.IsModal) {
                    return $window
                }
            }
        }
        Start-Sleep -Milliseconds 150
    } while ([DateTime]::UtcNow -lt $deadline)

    throw "Modal window '$Name' was not found."
}

function Set-ElementValue {
    param(
        [System.Windows.Automation.AutomationElement]$Element,
        [string]$Value
    )

    $pattern = $Element.GetCurrentPattern([System.Windows.Automation.ValuePattern]::Pattern)
    $pattern.SetValue($Value)
}

function Invoke-Element {
    param([System.Windows.Automation.AutomationElement]$Element)

    $pattern = $Element.GetCurrentPattern([System.Windows.Automation.InvokePattern]::Pattern)
    $pattern.Invoke()
}

function Click-Element {
    param(
        [System.Windows.Automation.AutomationElement]$Window,
        [System.Windows.Automation.AutomationElement]$Element
    )

    [PhoneFolderNativeUi]::SetForegroundWindow(
        [IntPtr]$Window.Current.NativeWindowHandle) | Out-Null
    $rectangle = $Element.Current.BoundingRectangle
    $x = [int](($rectangle.Left + $rectangle.Right) / 2)
    $y = [int](($rectangle.Top + $rectangle.Bottom) / 2)
    [PhoneFolderNativeUi]::SetCursorPos($x, $y) | Out-Null
    [PhoneFolderNativeUi]::mouse_event(0x0002, 0, 0, 0, [UIntPtr]::Zero)
    [PhoneFolderNativeUi]::mouse_event(0x0004, 0, 0, 0, [UIntPtr]::Zero)
}

function Wait-ForText {
    param(
        [System.Windows.Automation.AutomationElement]$Root,
        [string]$AutomationId,
        [scriptblock]$Predicate,
        [int]$TimeoutSeconds = 20
    )

    $deadline = [DateTime]::UtcNow.AddSeconds($TimeoutSeconds)
    do {
        $element = Find-Element -Root $Root -AutomationId $AutomationId -TimeoutSeconds 1
        $value = $element.Current.Name
        if (& $Predicate $value) {
            return $value
        }
        Start-Sleep -Milliseconds 200
    } while ([DateTime]::UtcNow -lt $deadline)

    throw "Text condition was not met for '$AutomationId'. Last value: '$value'."
}

function Get-DataItem {
    param(
        [System.Windows.Automation.AutomationElement]$Window,
        [string]$ItemName,
        [int]$TimeoutSeconds = 10
    )

    $deadline = [DateTime]::UtcNow.AddSeconds($TimeoutSeconds)
    do {
        $namedElement = $null
        try {
            $namedElement = Find-Element `
                -Root $Window `
                -Name $ItemName `
                -TimeoutSeconds 1
        } catch {
        }
        if ($null -ne $namedElement) {
            $current = $namedElement
            while ($null -ne $current) {
                if ($current.Current.ControlType -eq [System.Windows.Automation.ControlType]::DataItem) {
                    return $current
                }
                $current = [System.Windows.Automation.TreeWalker]::ControlViewWalker.GetParent($current)
            }
        }
        Start-Sleep -Milliseconds 200
    } while ([DateTime]::UtcNow -lt $deadline)

    throw "Data item '$ItemName' was not found."
}

function Select-DataItem {
    param([System.Windows.Automation.AutomationElement]$Item)

    $pattern = $Item.GetCurrentPattern([System.Windows.Automation.SelectionItemPattern]::Pattern)
    $pattern.Select()
}

function Complete-Prompt {
    param(
        [int]$ProcessId,
        [string]$Title,
        [string]$Value
    )

    $prompt = Get-ModalWindow -ProcessId $ProcessId -Name $Title
    try {
        $input = Find-Element `
            -Root $prompt `
            -AutomationId "ValueTextBox" `
            -TimeoutSeconds 2
    } catch {
        $input = Find-Element `
            -Root $prompt `
            -ControlType ([System.Windows.Automation.ControlType]::Edit)
    }
    Set-ElementValue -Element $input -Value $Value
    try {
        $ok = Find-Element `
            -Root $prompt `
            -Name "OK" `
            -ControlType ([System.Windows.Automation.ControlType]::Button) `
            -TimeoutSeconds 2
        Invoke-Element -Element $ok
    } catch {
        [PhoneFolderNativeUi]::SetForegroundWindow(
            [IntPtr]$prompt.Current.NativeWindowHandle) | Out-Null
        $input.SetFocus()
        Start-Sleep -Milliseconds 150
        [System.Windows.Forms.SendKeys]::SendWait("{ENTER}")
    }
}

function Confirm-Delete {
    param([int]$ProcessId)

    $dialog = Get-ModalWindow -ProcessId $ProcessId -Name "Confirm delete"
    $yes = Find-Element `
        -Root $dialog `
        -Name "Yes" `
        -ControlType ([System.Windows.Automation.ControlType]::Button)
    Invoke-Element -Element $yes
}

function Complete-FileDialog {
    param(
        [int]$ProcessId,
        [string]$Title,
        [string]$Path
    )

    $dialog = Get-ModalWindow -ProcessId $ProcessId -Name $Title
    $fileName = Find-Element -Root $dialog -AutomationId "1148"
    Set-ElementValue -Element $fileName -Value $Path
    $open = Find-Element `
        -Root $dialog `
        -Name "Open" `
        -ControlType ([System.Windows.Automation.ControlType]::Button)
    Invoke-Element -Element $open
}

function Complete-FolderDialog {
    param(
        [int]$ProcessId,
        [string]$Title,
        [string]$Path
    )

    $dialog = Get-ModalWindow -ProcessId $ProcessId -Name $Title
    $dialog.SetFocus()
    [System.Windows.Forms.SendKeys]::SendWait("^l")
    Start-Sleep -Milliseconds 250
    [System.Windows.Forms.SendKeys]::SendWait($Path)
    [System.Windows.Forms.SendKeys]::SendWait("{ENTER}")
    Start-Sleep -Milliseconds 500
    $select = Find-Element `
        -Root $dialog `
        -Name "Select Folder" `
        -ControlType ([System.Windows.Automation.ControlType]::Button)
    if (-not $select.Current.IsEnabled) {
        throw "The native Select Folder button was disabled."
    }
    Click-Element -Window $dialog -Element $select
    $deadline = [DateTime]::UtcNow.AddSeconds(5)
    do {
        Start-Sleep -Milliseconds 150
        try {
            $remaining = Get-ModalWindow `
                -ProcessId $ProcessId `
                -Name $Title `
                -TimeoutSeconds 1
        } catch {
            $remaining = $null
        }
    } while ($null -ne $remaining -and [DateTime]::UtcNow -lt $deadline)
    if ($null -ne $remaining) {
        throw "The native folder picker did not close after selecting the folder."
    }
}

function Start-TestApplication {
    $previousDisabledSetting = $env:PHONEFOLDER_DISABLE_REMEMBERED_DEVICE
    $previousCredentialTarget = $env:PHONEFOLDER_CREDENTIAL_TARGET
    Remove-Item Env:PHONEFOLDER_DISABLE_REMEMBERED_DEVICE -ErrorAction SilentlyContinue
    $env:PHONEFOLDER_CREDENTIAL_TARGET = $credentialTarget
    try {
        return Start-Process -FilePath $exe -PassThru
    } finally {
        if ($null -eq $previousDisabledSetting) {
            Remove-Item Env:PHONEFOLDER_DISABLE_REMEMBERED_DEVICE -ErrorAction SilentlyContinue
        } else {
            $env:PHONEFOLDER_DISABLE_REMEMBERED_DEVICE = $previousDisabledSetting
        }
        if ($null -eq $previousCredentialTarget) {
            Remove-Item Env:PHONEFOLDER_CREDENTIAL_TARGET -ErrorAction SilentlyContinue
        } else {
            $env:PHONEFOLDER_CREDENTIAL_TARGET = $previousCredentialTarget
        }
    }
}
$credentialTarget = "PhoneFolder/UI-Test-$([Guid]::NewGuid().ToString('N'))"
$process = Start-TestApplication
$createdFolder = "UI-QA-$([DateTime]::UtcNow.ToString("yyyyMMdd-HHmmss"))"
$renamedFolder = "$createdFolder-renamed"
$results = [System.Collections.Generic.List[string]]::new()

try {
    Set-TestStage "starting packaged application"
    if (-not $process.WaitForInputIdle(10000)) {
        throw "Phone Transfer did not become input-idle."
    }
    $window = Get-DesktopWindow -ProcessId $process.Id -Name "Phone Transfer"
    $windowHandle = [IntPtr]$window.Current.NativeWindowHandle
    $windowIcon = [PhoneFolderNativeUi]::SendMessage(
        $windowHandle,
        0x007F,
        [IntPtr]2,
        [IntPtr]::Zero)
    if ($windowIcon -eq [IntPtr]::Zero) {
        $windowIcon = [PhoneFolderNativeUi]::SendMessage(
            $windowHandle,
            0x007F,
            [IntPtr]0,
            [IntPtr]::Zero)
    }
    if ($windowIcon -eq [IntPtr]::Zero) {
        $windowIcon = [PhoneFolderNativeUi]::GetClassLongPtr($windowHandle, -14)
    }
    if ($windowIcon -eq [IntPtr]::Zero) {
        throw "The packaged window did not expose a taskbar icon."
    }
    $results.Add("The packaged window exposed the Phone Transfer taskbar icon.")
    $setupExpander = Find-Element `
        -Root $window `
        -Name "Setup and connection"
    $setupPattern = $setupExpander.GetCurrentPattern(
        [System.Windows.Automation.ExpandCollapsePattern]::Pattern)
    if ($setupPattern.Current.ExpandCollapseState -eq
        [System.Windows.Automation.ExpandCollapseState]::Collapsed) {
        $setupPattern.Expand()
        Start-Sleep -Milliseconds 300
    }
    $newFolderButton = Find-Element `
        -Root $window `
        -Name "New folder" `
        -ControlType ([System.Windows.Automation.ControlType]::Button)
    $hotspotButton = Find-Element `
        -Root $window `
        -AutomationId "HotspotButton" `
        -ControlType ([System.Windows.Automation.ControlType]::Button)
    if (-not $hotspotButton.Current.IsEnabled -or $hotspotButton.Current.IsOffscreen) {
        throw "The PC Hotspot connection button was not available."
    }
    if ($newFolderButton.Current.IsEnabled) {
        throw "File actions were enabled before connecting."
    }
    $results.Add("PC Hotspot mode was available and file actions remained disabled before connecting.")

    Set-TestStage "connecting"
    Set-ElementValue `
        -Element (Find-Element -Root $window -AutomationId "HostTextBox") `
        -Value $HostAddress
    Set-ElementValue `
        -Element (Find-Element -Root $window -AutomationId "PortTextBox") `
        -Value $Port.ToString()
    Set-ElementValue `
        -Element (Find-Element -Root $window -AutomationId "TokenTextBox") `
        -Value $AccessCode
    Start-Sleep -Milliseconds 500
    $hostValue = (Find-Element -Root $window -AutomationId "HostTextBox").GetCurrentPattern(
        [System.Windows.Automation.ValuePattern]::Pattern).Current.Value
    $tokenValue = (Find-Element -Root $window -AutomationId "TokenTextBox").GetCurrentPattern(
        [System.Windows.Automation.ValuePattern]::Pattern).Current.Value
    if ($hostValue -ne $HostAddress -or $tokenValue -ne $AccessCode) {
        throw "The Windows connection fields did not retain their automation values."
    }
    Invoke-Element -Element (Find-Element -Root $window -AutomationId "ConnectButton")
    $connection = Wait-ForText `
        -Root $window `
        -AutomationId "ConnectionStatusText" `
        -Predicate { param($value) $value.StartsWith("Connected to ") }
    Wait-ForText `
        -Root $window `
        -AutomationId "PathText" `
        -Predicate { param($value) $value -ne "Connect to a phone to browse files" } | Out-Null
    Wait-ForText `
        -Root $window `
        -AutomationId "OperationStatusText" `
        -Predicate { param($value) $value -match "^\d+ item\(s\)( \| .+)?$" } | Out-Null
    $newFolderButton = Find-Element `
        -Root $window `
        -Name "New folder" `
        -ControlType ([System.Windows.Automation.ControlType]::Button)
    if (-not $newFolderButton.Current.IsEnabled) {
        throw "File actions did not become enabled after root navigation."
    }
    $results.Add("Connected through packaged Windows UI: $connection")

    Set-TestStage "checking explorer controls"
    $folderTree = Find-Element `
        -Root $window `
        -AutomationId "FolderTree" `
        -ControlType ([System.Windows.Automation.ControlType]::Tree)
    if (-not $folderTree.Current.IsEnabled) {
        throw "The Explorer folder tree was not enabled after connecting."
    }
    $viewMode = Find-Element `
        -Root $window `
        -AutomationId "ViewModeButton" `
        -ControlType ([System.Windows.Automation.ControlType]::Button)
    $viewChecks = @(
        @{ Label = "Details"; Visible = "FilesGrid" }
        @{ Label = "List"; Visible = "FilesList" }
        @{ Label = "Thumbnails"; Visible = "ThumbnailList" }
        @{ Label = "Details"; Visible = "FilesGrid" }
    )
    foreach ($check in $viewChecks) {
        Invoke-Element -Element $viewMode
        $menuItem = Find-ProcessElement `
            -ProcessId $process.Id `
            -Name $check.Label `
            -ControlType ([System.Windows.Automation.ControlType]::MenuItem)
        Invoke-Element -Element $menuItem
        Start-Sleep -Milliseconds 300
        $visibleView = Find-Element -Root $window -AutomationId $check.Visible
        if ($visibleView.Current.IsOffscreen) {
            throw "View '$($check.Visible)' did not become visible."
        }
    }
    $paneToggle = Find-Element `
        -Root $window `
        -AutomationId "ConnectionPaneToggle" `
        -ControlType ([System.Windows.Automation.ControlType]::Button)
    Invoke-Element -Element $paneToggle
    Start-Sleep -Milliseconds 300
    $collapsedHost = $null
    try {
        $collapsedHost = Find-Element `
            -Root $window `
            -AutomationId "HostTextBox" `
            -TimeoutSeconds 1
    } catch {
    }
    if ($null -ne $collapsedHost -and -not $collapsedHost.Current.IsOffscreen) {
        throw "The connection panel did not collapse."
    }
    Invoke-Element -Element $paneToggle
    Start-Sleep -Milliseconds 300
    $expandedHost = Find-Element -Root $window -AutomationId "HostTextBox"
    if ($expandedHost.Current.IsOffscreen) {
        throw "The connection panel did not expand."
    }
    $folderPaneToggle = Find-Element `
        -Root $window `
        -AutomationId "FolderPaneButton" `
        -ControlType ([System.Windows.Automation.ControlType]::Button)
    Invoke-Element -Element $folderPaneToggle
    Start-Sleep -Milliseconds 300
    $folderPaneToggle = Find-Element `
        -Root $window `
        -Name "Folders: Off" `
        -ControlType ([System.Windows.Automation.ControlType]::Button)
    Invoke-Element -Element $folderPaneToggle
    Start-Sleep -Milliseconds 300
    Find-Element `
        -Root $window `
        -Name "Copy to" `
        -ControlType ([System.Windows.Automation.ControlType]::Button) | Out-Null
    $results.Add("Folder tree, folder-pane collapse, copy action, all view modes, and connection-panel collapse worked.")

    Set-TestStage "verifying trusted reconnect"
    Set-ElementValue `
        -Element (Find-Element -Root $window -AutomationId "TokenTextBox") `
        -Value "00000000"
    Start-Sleep -Milliseconds 500
    Invoke-Element -Element (Find-Element -Root $window -AutomationId "ConnectButton")
    Wait-ForText `
        -Root $window `
        -AutomationId "OperationStatusText" `
        -Predicate { param($value) $value -match "^\d+ item\(s\)( \| .+)?$" } | Out-Null
    $trustedConnection = (Find-Element `
        -Root $window `
        -AutomationId "ConnectionStatusText").Current.Name
    Set-ElementValue `
        -Element (Find-Element -Root $window -AutomationId "TokenTextBox") `
        -Value $AccessCode
    $newFolderButton = Find-Element `
        -Root $window `
        -Name "New folder" `
        -ControlType ([System.Windows.Automation.ControlType]::Button)
    if (-not $newFolderButton.Current.IsEnabled) {
        throw "File actions were unavailable after trusted reconnect."
    }
    $trustedDevices = Find-Element `
        -Root $window `
        -AutomationId "TrustedDevicesCombo" `
        -ControlType ([System.Windows.Automation.ControlType]::ComboBox)
    $switchDevice = Find-Element `
        -Root $window `
        -AutomationId "SwitchDeviceButton" `
        -ControlType ([System.Windows.Automation.ControlType]::Button)
    if (-not $trustedDevices.Current.IsEnabled -or -not $switchDevice.Current.IsEnabled) {
        throw "Trusted phone switching controls were unavailable after pairing."
    }
    Invoke-Element -Element (Find-Element `
        -Root $window `
        -AutomationId "ForgetDeviceButton" `
        -ControlType ([System.Windows.Automation.ControlType]::Button))
    $trustedWindow = Get-ModalWindow `
        -ProcessId $process.Id `
        -Name "Trusted phones"
    $trustedList = Find-Element `
        -Root $trustedWindow `
        -AutomationId "ProfilesGrid" `
        -ControlType ([System.Windows.Automation.ControlType]::DataGrid)
    if (-not $trustedList.Current.IsEnabled) {
        throw "The trusted-phone manager list was unavailable."
    }
    $closeButtons = $trustedWindow.FindAll(
        [System.Windows.Automation.TreeScope]::Descendants,
        [System.Windows.Automation.AndCondition]::new(
            [System.Windows.Automation.PropertyCondition]::new(
                [System.Windows.Automation.AutomationElement]::NameProperty,
                "Close"),
            [System.Windows.Automation.PropertyCondition]::new(
                [System.Windows.Automation.AutomationElement]::ControlTypeProperty,
                [System.Windows.Automation.ControlType]::Button)))
    $contentClose = $null
    for ($index = 0; $index -lt $closeButtons.Count; $index++) {
        if ($closeButtons.Item($index).Current.ClassName -eq "Button") {
            $contentClose = $closeButtons.Item($index)
            break
        }
    }
    if ($null -eq $contentClose) {
        throw "The trusted-phone manager Close button was not found."
    }
    Invoke-Element -Element $contentClose
    $trustedCloseDeadline = [DateTime]::UtcNow.AddSeconds(5)
    do {
        Start-Sleep -Milliseconds 150
        try {
            $remainingTrustedWindow = Get-ModalWindow `
                -ProcessId $process.Id `
                -Name "Trusted phones" `
                -TimeoutSeconds 1
        } catch {
            $remainingTrustedWindow = $null
        }
    } while ($null -ne $remainingTrustedWindow -and
        [DateTime]::UtcNow -lt $trustedCloseDeadline)
    if ($null -ne $remainingTrustedWindow) {
        throw "The trusted-phone manager did not close."
    }
    $results.Add("Trusted reconnect worked after changing the access-code field: $trustedConnection")
    $results.Add("The full trusted-phone manager opened from the Setup section.")

    Set-TestStage "creating folder"
    if (-not $newFolderButton.Current.IsEnabled -or $newFolderButton.Current.IsOffscreen) {
        throw "The New folder button was not available after root navigation completed."
    }
    Click-Element -Window $window -Element $newFolderButton
    Complete-Prompt -ProcessId $process.Id -Title "New folder" -Value $createdFolder
    $createdItem = Get-DataItem -Window $window -ItemName $createdFolder
    $results.Add("Created a remote folder through the Windows prompt.")

    Set-TestStage "renaming folder"
    Select-DataItem -Item $createdItem
    Invoke-Element -Element (Find-Element `
        -Root $window `
        -Name "Rename" `
        -ControlType ([System.Windows.Automation.ControlType]::Button))
    Complete-Prompt -ProcessId $process.Id -Title "Rename" -Value $renamedFolder
    $renamedItem = Get-DataItem -Window $window -ItemName $renamedFolder
    $results.Add("Renamed the remote folder through the Windows prompt.")

    Set-TestStage "uploading file"
    Invoke-Element -Element (Find-Element `
        -Root $window `
        -Name "Upload files" `
        -ControlType ([System.Windows.Automation.ControlType]::Button))
    Complete-FileDialog `
        -ProcessId $process.Id `
        -Title "Choose files to upload" `
        -Path $sourcePath
    Wait-ForText `
        -Root $window `
        -AutomationId "OperationStatusText" `
        -Predicate {
            param($value)
            $value.StartsWith("Uploaded ") -or $value.StartsWith("Copied ")
        } | Out-Null
    Invoke-Element -Element (Find-Element `
        -Root $window `
        -Name "Refresh" `
        -ControlType ([System.Windows.Automation.ControlType]::Button))
    Wait-ForText `
        -Root $window `
        -AutomationId "OperationStatusText" `
        -Predicate { param($value) $value -match "^\d+ item\(s\)( \| .+)?$" } | Out-Null
    $uploadedItem = Get-DataItem -Window $window -ItemName (Split-Path -Leaf $sourcePath)
    $results.Add("Uploaded a file through the native Windows file picker.")

    Set-TestStage "opening direct image preview"
    Invoke-Element -Element (Find-Element `
        -Root $window `
        -Name "Upload files" `
        -ControlType ([System.Windows.Automation.ControlType]::Button))
    Complete-FileDialog `
        -ProcessId $process.Id `
        -Title "Choose files to upload" `
        -Path $previewPath
    Wait-ForText `
        -Root $window `
        -AutomationId "OperationStatusText" `
        -Predicate {
            param($value)
            $value.StartsWith("Uploaded ") -or $value.StartsWith("Copied ")
        } | Out-Null
    Invoke-Element -Element (Find-Element `
        -Root $window `
        -Name "Refresh" `
        -ControlType ([System.Windows.Automation.ControlType]::Button))
    Wait-ForText `
        -Root $window `
        -AutomationId "OperationStatusText" `
        -Predicate { param($value) $value -match "^\d+ item\(s\)( \| .+)?$" } | Out-Null
    $previewItem = Get-DataItem -Window $window -ItemName (Split-Path -Leaf $previewPath)
    Select-DataItem -Item $previewItem
    $openMedia = Find-Element `
        -Root $window `
        -AutomationId "OpenMediaButton" `
        -ControlType ([System.Windows.Automation.ControlType]::Button)
    if (-not $openMedia.Current.IsEnabled) {
        throw "Open / Play was not enabled for the uploaded image."
    }
    Invoke-Element -Element $openMedia
    $previewTitle = "$(Split-Path -Leaf $previewPath) - Phone Transfer"
    $previewWindow = Find-Element `
        -Root $window `
        -Name $previewTitle `
        -ControlType ([System.Windows.Automation.ControlType]::Window)
    $previewStatus = Find-Element `
        -Root $previewWindow `
        -Name "Image loaded directly from the phone" `
        -ControlType ([System.Windows.Automation.ControlType]::Text)
    if ($previewStatus.Current.IsOffscreen) {
        throw "The direct image preview status was not visible."
    }
    foreach ($controlName in @("Previous", "Next", "Rotate", "Full screen")) {
        Find-Element `
            -Root $previewWindow `
            -Name $controlName `
            -ControlType ([System.Windows.Automation.ControlType]::Button) | Out-Null
    }
    $previewWindowPattern = $previewWindow.GetCurrentPattern(
        [System.Windows.Automation.WindowPattern]::Pattern)
    $previewWindowPattern.Close()
    $results.Add("Opened an image with previous, next, rotate, and fullscreen controls without a PC copy.")

    if (-not $process.HasExited) {
        $process.Kill()
        $process.WaitForExit()
    }

    Set-TestStage "verifying uploaded file"
    $apiHost = if ($HostAddress.Contains(":") -and -not $HostAddress.StartsWith("[")) {
        "[$HostAddress]"
    } else {
        $HostAddress
    }
    $baseUrl = "https://${apiHost}:$Port/api/v1"
    $rootJson = & curl.exe -sk `
        -H "X-PhoneFolder-Token: $AccessCode" `
        "$baseUrl/roots"
    if ($LASTEXITCODE -ne 0) {
        throw "Could not list the remote root after the packaged UI upload."
    }
    $rootItem = ($rootJson | ConvertFrom-Json)[0]
    $childrenJson = & curl.exe -sk `
        -H "X-PhoneFolder-Token: $AccessCode" `
        "$baseUrl/items/$($rootItem.id)/children"
    if ($LASTEXITCODE -ne 0) {
        throw "Could not list the uploaded packaged UI file."
    }
    $remoteUploaded = $null
    foreach ($candidate in ($childrenJson | ConvertFrom-Json)) {
        if ($candidate.name -eq (Split-Path -Leaf $sourcePath)) {
            $remoteUploaded = $candidate
            break
        }
    }
    if ($null -eq $remoteUploaded) {
        throw "The packaged UI upload was not present on the phone."
    }
    $downloadedPath = Join-Path $downloadRoot (Split-Path -Leaf $sourcePath)
    [System.Net.ServicePointManager]::SecurityProtocol = [System.Net.SecurityProtocolType]::Tls12
    [System.Net.ServicePointManager]::ServerCertificateValidationCallback = { $true }
    $webClient = [System.Net.WebClient]::new()
    try {
        $webClient.Headers.Add("X-PhoneFolder-Token", $AccessCode)
        $webClient.DownloadFile(
            "$baseUrl/items/$($remoteUploaded.id)/content",
            $downloadedPath)
    } finally {
        $webClient.Dispose()
    }
    if (-not (Test-Path -LiteralPath $downloadedPath)) {
        throw "Transfer verification did not create '$downloadedPath'."
    }
    $sourceHash = (Get-FileHash -LiteralPath $sourcePath -Algorithm SHA256).Hash
    $downloadedHash = (Get-FileHash -LiteralPath $downloadedPath -Algorithm SHA256).Hash
    if ($sourceHash -ne $downloadedHash) {
        throw "The UI upload/download round trip changed file contents."
    }
    $results.Add("The packaged UI upload downloaded through the local API with matching SHA-256.")

    Set-TestStage "cleaning test items"
    $childrenJson = & curl.exe -sk `
        -H "X-PhoneFolder-Token: $AccessCode" `
        "$baseUrl/items/$($rootItem.id)/children"
    if ($LASTEXITCODE -ne 0) {
        throw "Could not list remote items for UI test cleanup."
    }
    $cleanupNames = @(
        $renamedFolder,
        (Split-Path -Leaf $sourcePath),
        (Split-Path -Leaf $previewPath))
    foreach ($remoteItem in ($childrenJson | ConvertFrom-Json)) {
        if ($cleanupNames -contains $remoteItem.name) {
            & curl.exe -sk `
                -X DELETE `
                -H "X-PhoneFolder-Token: $AccessCode" `
                "$baseUrl/items/$($remoteItem.id)" | Out-Null
            if ($LASTEXITCODE -ne 0) {
                throw "Could not remove remote UI test item '$($remoteItem.name)'."
            }
        }
    }
    $results.Add("Cleaned the packaged UI test items through the authenticated local API.")

    Set-TestStage "verifying automatic reconnect"
    if (-not $process.HasExited) {
        $process.Kill()
        $process.WaitForExit()
    }
    $process = Start-TestApplication
    if (-not $process.WaitForInputIdle(10000)) {
        throw "Phone Transfer did not become input-idle after reopening."
    }
    $window = Get-DesktopWindow -ProcessId $process.Id -Name "Phone Transfer"
    $automaticConnection = Wait-ForText `
        -Root $window `
        -AutomationId "ConnectionStatusText" `
        -Predicate { param($value) $value.StartsWith("Connected to ") } `
        -TimeoutSeconds 20
    $results.Add("Reopened and automatically reconnected through Windows Credential Manager: $automaticConnection")

    Set-TestStage "complete"
    Write-Output "Phone Transfer packaged Windows UI verification passed:"
    foreach ($result in $results) {
        Write-Output "  PASS: $result"
    }
    Write-Output "  Artifacts: $artifactRoot"
} finally {
    if (-not $process.HasExited) {
        $process.Kill()
        $process.WaitForExit()
    }
    & cmdkey.exe "/delete:$credentialTarget" 2>$null | Out-Null
}
