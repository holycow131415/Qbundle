Imports System.Globalization
Imports System.IO
Imports System.Reflection
Imports System.Threading
Imports Chromium
Imports Chromium.WebBrowser

Public Class frmMain
    Private Delegate Sub DUpdate([AppId] As Integer, [Operation] As Integer, [data] As String)

    Private Delegate Sub DStarting([AppId] As Integer)

    Private Delegate Sub DStoped([AppId] As Integer)

    Private Delegate Sub DAborted([AppId] As Integer, [data] As String)

    Private Delegate Sub DAPIResult([Height] As String, [TimeStamp] As String)

    Private Delegate Sub DHttpResult([Data] As String)

    Private Delegate Sub DNewUpdatesAvilable()

    Private WB1 As ChromiumWebBrowser

    Public Console(1) As List(Of String)
    Public Running As Boolean
    Public FullySynced As Boolean
    Public Updateinfo As String
    Public Repositories() As String
    Private LastException As Date
    Private WithEvents APITimer As System.Windows.Forms.Timer
    Private WithEvents ShutdownWallet As System.Windows.Forms.Timer
    Private WithEvents PasswordTimer As System.Windows.Forms.Timer
    Private WithEvents OneMinCron As System.Windows.Forms.Timer

    Private CurHeight As Integer
    Private LastShowHeight As Integer
    Private LastMinuteHeight As Integer
    Private WithEvents BlockMinute As New System.Windows.Forms.Timer

#Region " Form Events "

    Private Sub frmMain_Load(sender As Object, e As EventArgs) Handles MyBase.Load

        If Generic.CheckDotNet() = False Then
            MsgBox("You need to install .net 4.5.2.")
        End If

        Q = New clsQ
        Generic.CheckCommandArgs()
        If Q.settings.AlwaysAdmin And Not Generic.IsAdmin Then
            Generic.RestartAsAdmin()
            End
        End If
        If Generic.DriveCompressed(QGlobal.BaseDir) Then
            Dim Msg = "You are running Qbundle on a NTFS compressed drive or folder."
            Msg &= " This is not supported and may cause unstable environment." & vbCrLf & vbCrLf
            Msg &= "Please move Qbundle to another drive or decompress the drive or folder."
            MsgBox(Msg, MsgBoxStyle.Critical Or MsgBoxStyle.OkOnly, "Compressed drive")

        End If

        If Q.settings.DebugMode = True Then Generic.DebugMe = True
        LastException = Now 'for brs exception monitoring
        If Not Generic.CheckWritePermission Then
            MsgBox(
                "Qbundle does not have writepermission to it's own folder. Please move to another location or change the permissions.",
                MsgBoxStyle.Critical Or MsgBoxStyle.OkOnly, "Permissions")
            End
        End If

        For i = 0 To UBound(Console)
            Console(i) = New List(Of String)
        Next

        Generic.CheckUpgrade() 'if there is any upgradescenarios


        'Check Core installation. If not satisfying then run checkenv dialog

        If Not CheckEnvironment() Then
            frmFirstTime.ShowDialog()
        End If

        If Q.settings.FirstRun Then
            End 'we have canceled environment screen
        End If

        If Q.settings.CheckForUpdates Then
            Q.AppManager.StartUpdateNotifications()
            AddHandler Q.AppManager.UpdateAvailable, AddressOf NewUpdatesAvilable
        End If

        SetDbInfo()

        If Q.settings.Cpulimit = 0 Or Q.settings.Cpulimit > Environment.ProcessorCount Then 'need to set correct cpu
            Select Case Environment.ProcessorCount
                Case 1
                    Q.settings.Cpulimit = 1
                Case 2
                    Q.settings.Cpulimit = 1
                Case 4
                    Q.settings.Cpulimit = 3
                Case Else
                    Q.settings.Cpulimit = Environment.ProcessorCount - 2
            End Select
        End If
        APITimer = New System.Windows.Forms.Timer
        ShutdownWallet = New System.Windows.Forms.Timer
        PasswordTimer = New System.Windows.Forms.Timer
        OneMinCron = New System.Windows.Forms.Timer


        AddHandler Q.ProcHandler.Started, AddressOf Starting
        AddHandler Q.ProcHandler.Stopped, AddressOf Stopped
        AddHandler Q.ProcHandler.Update, AddressOf ProcEvents
        AddHandler Q.ProcHandler.Aborting, AddressOf Aborted

        AddHandler Q.Service.Stopped, AddressOf Stopped
        AddHandler Q.Service.Update, AddressOf ProcEvents

        'Init Browser
        Try
            ChromiumWebBrowser.Initialize()
        Catch ex As CfxException
            If Q.AppManager.InstallApp("chromium") Then
                Generic.RestartBundle()
                Close()
            Else
                Close()
            End If
        End Try
        WB1 = New ChromiumWebBrowser()

        SetLoginMenu()

        If Q.settings.QBMode = 0 Then
            If Q.settings.AutoStart Then
                StartWallet()
            End If
        End If
        SetMode(Q.settings.QBMode)

        pnlAIO.Controls.Add(WB1)
        WB1.Dock = DockStyle.Fill


        Generic.UpdateLocalWallet()
        For t = 0 To UBound(Q.AppManager.AppStore.Wallets)
            cmbSelectWallet.Items.Add(Q.AppManager.AppStore.Wallets(t).Name)
        Next
        cmbSelectWallet.SelectedIndex = 0
        OneMinCron.Interval = 60000
        OneMinCron.Enabled = True
        OneMinCron.Start()

        If Q.settings.GetCoinMarket Then
            Dim trda As Thread
            trda = New Thread(AddressOf FetchCoinMarket)
            trda.IsBackground = True
            trda.Start()
            trda = Nothing
        End If

        SetWalletInfo()
    End Sub

    Friend Sub SetWalletInfo()


        Dim Title = "Qbundle v"
        Title &= Assembly.GetExecutingAssembly.GetName.Version.Major & "." &
                 Assembly.GetExecutingAssembly.GetName.Version.Minor & "." &
                 Assembly.GetExecutingAssembly.GetName.Version.Revision & " | "
        Title &= "Burstcoin Wallet v" & Q.AppManager.GetInstalledVersion("BRS", True)
        If Generic.DebugMe Then Title &= " (DebugMode)"
        Text = Title
        lblWallet.Text = "Burst wallet v" & Q.AppManager.GetInstalledVersion("BRS", True)
    End Sub

    Private Sub SetMode(NewMode As Integer)
        Select Case NewMode
            Case 0 ' AIO Mode
                FormBorderStyle = FormBorderStyle.Sizable
                MaximizeBox = True
                Width = 1024
                Height = 760
                If Height > My.Computer.Screen.WorkingArea.Height - 50 Then
                    Height = My.Computer.Screen.WorkingArea.Height - 50
                End If
                If Width > My.Computer.Screen.WorkingArea.Width - 50 Then
                    Width = My.Computer.Screen.WorkingArea.Width - 50
                End If
                Q.settings.QBMode = 0
                Q.settings.SaveSettings()
                Top = (My.Computer.Screen.WorkingArea.Height\2) - (Height\2)
                Left = (My.Computer.Screen.WorkingArea.Width\2) - (Width\2)
                MenuBar.Visible = True
                pnlAIO.Visible = True
                pnlAIO.Top = MenuBar.Top + MenuBar.Height
                pnlAIO.Left = 0
                pnlAIO.Height = StatusStrip1.Top - 50
                pnlAIO.Width = Width - 17
                pnlAIO.Anchor = AnchorStyles.Bottom Or AnchorStyles.Left Or AnchorStyles.Top Or AnchorStyles.Right
                pnlLauncher.Visible = False
                lblWalletIS.Visible = True
                lblSplitterWallet.Visible = True
                lblWalletStatus.Visible = True
                WalletModeToolStripMenuItem1.Checked = True
                LauncherModeToolStripMenuItem1.Checked = False
                lblSelectWallet.Visible = True
                cmbSelectWallet.Visible = True
            Case 1 ' Launcher Mode
                FormBorderStyle = FormBorderStyle.FixedDialog
                MaximizeBox = False

                Dim g As Graphics = CreateGraphics()
                Dim dpiX As Decimal = CDec(g.DpiX)/100
                Dim dpiY As Decimal = CDec(g.DpiY)/100
                If dpiY > 1 Then
                    dpiX = 1 - (dpiX - 1)
                    dpiY = 1 - (dpiY - 1)
                    Dim res As New SizeF(dpiX, dpiY)
                    Scale(res)
                End If

                Width = pnlLauncher.Left + pnlLauncher.Width + 24
                Height = pnlLauncher.Top + pnlLauncher.Height + 70

                Q.settings.QBMode = 1
                Q.settings.SaveSettings()
                Top = (My.Computer.Screen.WorkingArea.Height\2) - (Height\2)
                Left = (My.Computer.Screen.WorkingArea.Width\2) - (Width\2)
                pnlAIO.Visible = False
                pnlLauncher.Visible = True
                pnlLauncher.BringToFront()
                lblWalletIS.Visible = False
                lblSplitterWallet.Visible = False
                lblWalletStatus.Visible = False
                '   MenuBar.Visible = False
                WalletModeToolStripMenuItem1.Checked = False
                LauncherModeToolStripMenuItem1.Checked = True
                lblSelectWallet.Visible = False
                cmbSelectWallet.Visible = False
        End Select
    End Sub

    Private Sub frmMain_FormClosing(sender As Object, e As FormClosingEventArgs) Handles Me.FormClosing
        Try
            If Running Then
                e.Cancel = True
                If e.CloseReason = CloseReason.UserClosing And
                   MsgBox("Do you want to shutdown the wallet?", MsgBoxStyle.YesNo, "Exit") = MsgBoxResult.No Then
                    Exit Sub
                End If
                ShutdownWallet.Interval = 100
                ShutdownWallet.Enabled = True
                ShutdownWallet.Start()

                StopWallet()
                frmShutdown.Show()
                Hide()
                WB1.Dispose()
                Exit Sub
            End If
        Catch ex As Exception
            Generic.WriteDebug(ex)
        End Try
    End Sub

    Private Sub frmMain_Resize(sender As Object, e As EventArgs) Handles Me.Resize
        If WindowState = FormWindowState.Minimized Then
            Try
                If Q.settings.MinToTray Then
                    TrayIcon.Visible = True
                    ShowInTaskbar = False
                End If
            Catch ex As Exception
            End Try
        End If
    End Sub

    Private Sub ShudownWallet_tick(sender As Object, e As EventArgs) Handles ShutdownWallet.Tick
        If Running = False Then
            Close()
        End If
    End Sub

    Private Sub PasswordTimer_tick(sender As Object, e As EventArgs) Handles PasswordTimer.Tick
        My.Computer.Clipboard.SetText("-")
        PasswordTimer.Stop()
        PasswordTimer.Enabled = False
    End Sub

    Private Sub PrepareUpdate()
        Dim f As New frmUpdate

        If f.ShowDialog = DialogResult.Yes Then
            ShutdownOnUpdate()
        End If
    End Sub

    Private Sub ShutdownOnUpdate()


        RemoveHandler Q.ProcHandler.Started, AddressOf Starting
        RemoveHandler Q.ProcHandler.Stopped, AddressOf Stopped
        RemoveHandler Q.ProcHandler.Update, AddressOf ProcEvents
        RemoveHandler Q.ProcHandler.Aborting, AddressOf Aborted
        RemoveHandler Q.Service.Stopped, AddressOf Stopped
        RemoveHandler Q.Service.Update, AddressOf ProcEvents

        Dim p = New Process
        p.StartInfo.WorkingDirectory = QGlobal.BaseDir
        p.StartInfo.Arguments = "BWLUpdate" & " " & Path.GetFileName(Application.ExecutablePath)
        p.StartInfo.UseShellExecute = True
        p.StartInfo.FileName = QGlobal.BaseDir & "Updater.exe"
        p.Start()
        p.Dispose()
        Thread.Sleep(500)


        End
    End Sub

#End Region


#Region " Clickabe Objects "
    'buttons
    Private Sub btnStartStop_Click(sender As Object, e As EventArgs) Handles btnStartStop.Click
        StartStop()
    End Sub

    Private Sub StartStop()
        If Running Then
            StopWallet()
        Else
            StartWallet()
        End If
    End Sub

    'labels
    Private Sub lblGotoWallet_LinkClicked(sender As Object, e As LinkLabelLinkClickedEventArgs) _
        Handles lblGotoWallet.LinkClicked

        Dim s() As String = Split(Q.settings.ListenIf, ";")
        Dim url As String = Nothing
        If s(0) = "0.0.0.0" Then
            url = "http://127.0.0.1:" & s(1)
        Else
            url = "http://" & s(0) & ":" & s(1)
        End If
        Process.Start(url)
    End Sub

    Private Sub lblUpdates_Click(sender As Object, e As EventArgs) Handles lblUpdates.Click
        PrepareUpdate()
    End Sub
    'toolstrips
    Private Sub ExitToolStripMenuItem1_Click(sender As Object, e As EventArgs) Handles ExitToolStripMenuItem1.Click
        Close()
    End Sub

    Private Sub SettingsToolStripMenuItem_Click(sender As Object, e As EventArgs) _
        Handles SettingsToolStripMenuItem.Click

        frmSettings.Show()
        frmSettings.Focus()
    End Sub

    Private Sub ContributorsToolStripMenuItem_Click(sender As Object, e As EventArgs) _
        Handles ContributorsToolStripMenuItem.Click
        frmContributors.Show()
        frmContributors.Focus()
    End Sub

    Private Sub CheckForUpdatesToolStripMenuItem_Click(sender As Object, e As EventArgs) _
        Handles CheckForUpdatesToolStripMenuItem.Click
        PrepareUpdate()
    End Sub

    Private Sub ChangeDatabaseToolStripMenuItem_Click(sender As Object, e As EventArgs) _
        Handles ChangeDatabaseToolStripMenuItem.Click

        frmChangeDatabase.Show()
        frmChangeDatabase.Focus()
    End Sub

    Private Sub ExportDatabaseToolStripMenuItem_Click(sender As Object, e As EventArgs)
        frmExportDb.Show()
        frmExportDb.Focus()
    End Sub

    Private Sub ImportDatabaseToolStripMenuItem1_Click(sender As Object, e As EventArgs) _
        Handles ImportDatabaseToolStripMenuItem1.Click
        frmImport.Show()
        frmImport.Focus()
    End Sub

    Private Sub DeveloperToolStripMenuItem_Click(sender As Object, e As EventArgs) _
        Handles DeveloperToolStripMenuItem.Click
        frmDeveloper.Show()
        frmDeveloper.Focus()
    End Sub

#End Region

#Region " Wallet Events "

    Private Sub Starting(AppId As Integer)
        If InvokeRequired Then
            Dim d As New DStarting(AddressOf Starting)
            Invoke(d, New Object() {AppId})
            Return
        End If

        Select Case AppId
            Case QGlobal.AppNames.BRS
                lblNrsStatus.Text = "Starting"
                lblNrsStatus.ForeColor = Color.DarkOrange
                lblWalletStatus.Text = "Starting"
            Case QGlobal.AppNames.MariaPortable
                LblDbStatus.Text = "Starting"
                LblDbStatus.ForeColor = Color.DarkOrange
        End Select
    End Sub

    Private Sub Stopped(AppId As Integer)
        If InvokeRequired Then
            Dim d As New DStoped(AddressOf Stopped)
            Invoke(d, New Object() {AppId})
            Return
        End If

        If AppId = QGlobal.AppNames.BRS Then
            lblNrsStatus.Text = "Stopped"
            lblNrsStatus.ForeColor = Color.Red
        End If
        If Q.settings.DbType = QGlobal.DbType.pMariaDB Then
            If AppId = QGlobal.AppNames.MariaPortable Then
                LblDbStatus.Text = "Stopped"
                LblDbStatus.ForeColor = Color.Red
                UpdateUIState(QGlobal.ProcOp.Stopped)
            End If
        Else
            UpdateUIState(QGlobal.ProcOp.Stopped)
        End If
    End Sub

    Private Sub UpdateUIState(State As Integer)

        Select Case State
            Case QGlobal.ProcOp.Stopped
                Running = False
                btnStartStop.Text = "Start wallet"
                btnStartStop.Enabled = True
                tsStartStop.Enabled = True
                tsStartStop.Text = "Start wallet"
                lblWalletStatus.Text = "Stopped"
                lblGotoWallet.Visible = False
                StopAPIFetch()

                WB1.LoadString(My.Resources.stoppedscreen)


            Case QGlobal.ProcOp.FoundSignal ' Running

                btnStartStop.Text = "Stop wallet"
                btnStartStop.Enabled = True

                tsStartStop.Enabled = True
                tsStartStop.Text = "Stop wallet"

                lblWalletStatus.Text = "Stopped"

                lblNrsStatus.Text = "Running"
                lblNrsStatus.ForeColor = Color.DarkGreen
                lblWalletStatus.Text = "Running"
                Running = True
                lblGotoWallet.Visible = True


        End Select
    End Sub


    Private Sub ProcEvents(AppId As Integer, Operation As Integer, data As String)
        If InvokeRequired Then
            Dim d As New DUpdate(AddressOf ProcEvents)
            Invoke(d, New Object() {AppId, Operation, data})
            Return
        End If
        'threadsafe here
        Select Case Operation
            Case QGlobal.ProcOp.Stopped 'Stoped
                '   If AppId = QGlobal.AppNames.BRS Then
                '   LblDbStatus.Text = "Stopped"
                '   LblDbStatus.ForeColor = Color.Red
                '   lblWalletStatus.Text = "Stopped"
                '   End If
                '   If AppId = QGlobal.AppNames.MariaPortable Then
                '   lblNrsStatus.Text = "Stopped"
                '   lblNrsStatus.ForeColor = Color.Red
                '   lblWalletStatus.Text = "Stopped"
                '   End If


            Case QGlobal.ProcOp.FoundSignal
                If AppId = QGlobal.AppNames.MariaPortable Then
                    LblDbStatus.Text = "Running"
                    LblDbStatus.ForeColor = Color.DarkGreen

                End If
                If AppId = QGlobal.AppNames.BRS Then

                    UpdateUIState(QGlobal.ProcOp.FoundSignal)

                    StartAPIFetch()
                    Dim s() As String = Split(Q.settings.ListenIf, ";")
                    Dim url As String = Nothing
                    If s(0) = "0.0.0.0" Then
                        url = "http://127.0.0.1:" & s(1)
                    Else
                        url = "http://" & s(0) & ":" & s(1)
                    End If

                    WB1.LoadUrl(url & "?refreshToken=" + Guid.NewGuid().ToString())
                End If
            Case QGlobal.ProcOp.Stopping
                If AppId = QGlobal.AppNames.MariaPortable Then
                    LblDbStatus.Text = "Stopping"
                    LblDbStatus.ForeColor = Color.DarkOrange
                    WB1.LoadString(My.Resources.Stoppingscreen)
                End If

                If AppId = QGlobal.AppNames.BRS Then
                    lblNrsStatus.Text = "Stopping"
                    lblNrsStatus.ForeColor = Color.DarkOrange
                    lblWalletStatus.Text = "Stopping"
                    WB1.LoadString(My.Resources.Stoppingscreen)
                End If
            Case QGlobal.ProcOp.ConsoleOut
                If AppId = QGlobal.AppNames.MariaPortable Then
                    Console(1).Add(data)
                    If Console(1).Count = 3001 Then Console(1).RemoveAt(0)
                End If
                If AppId = QGlobal.AppNames.BRS Then
                    Console(0).Add(data)
                    'here we can do error detection
                    If Q.settings.WalletException And LastException.AddHours(1) < Now Then
                        If data.StartsWith("Exception in") Or data.StartsWith("java.lang.RuntimeException") Then
                            LastException = Now
                            Q.ProcHandler.ReStartProcess(QGlobal.AppNames.BRS)
                        End If
                    End If

                    If Console(0).Count = 3001 Then Console(0).RemoveAt(0)
                End If
            Case QGlobal.ProcOp.ConsoleErr
                If AppId = QGlobal.AppNames.MariaPortable Then
                    Console(1).Add(data)
                    If Console(1).Count = 3001 Then Console(1).RemoveAt(0)
                End If
                If AppId = QGlobal.AppNames.BRS Then
                    Console(0).Add(data)
                    'here we can do error detection
                    If Q.settings.WalletException And LastException.AddHours(1) < Now Then
                        If data.StartsWith("Exception in") Or data.StartsWith("java.lang.RuntimeException") Then
                            LastException = Now
                            Q.ProcHandler.ReStartProcess(QGlobal.AppNames.BRS)
                        End If
                    End If

                    If Console(0).Count = 3001 Then Console(0).RemoveAt(0)
                End If

            Case QGlobal.ProcOp.Err 'Error
                MsgBox(
                    "A Unhandled error happend when services tried to start. Console view might give clue to what is wrong. Some services might still be running.",
                    MsgBoxStyle.Critical Or MsgBoxStyle.OkOnly, "Error")
                Running = False
        End Select
    End Sub

    Private Sub Aborted(AppId As Integer, Data As String)
        If InvokeRequired Then
            Dim d As New DAborted(AddressOf Aborted)
            Invoke(d, New Object() {AppId, Data})
            Return
        End If

        If AppId = QGlobal.AppNames.BRS Then
            lblNrsStatus.Text = "Stopped"
            lblNrsStatus.ForeColor = Color.Red
            lblWalletStatus.Text = "Stopped"
        End If
        If Q.settings.DbType = QGlobal.DbType.pMariaDB Then
            If AppId = QGlobal.AppNames.MariaPortable Then
                LblDbStatus.Text = "Stopped"
                LblDbStatus.ForeColor = Color.Red
                UpdateUIState(QGlobal.ProcOp.Stopped)
            End If
        Else
            UpdateUIState(QGlobal.ProcOp.Stopped)
        End If
    End Sub

    Friend Sub StartWallet(Optional ByVal WriteDebug As Boolean = False)
        WB1.LoadString(My.Resources.loadscreen)
        If Not Generic.SanityCheck() Then
            UpdateUIState(QGlobal.ProcOp.Stopped)
            Exit Sub
        End If
        Generic.WriteWalletConfig(WriteDebug)
        If Q.Service.IsInstalled Then
            Q.Service.StartService()
        Else
            If Q.settings.DbType = QGlobal.DbType.pMariaDB Then 'send startsequence
                Dim pset(1) As clsProcessHandler.pSettings
                pset(0) = New clsProcessHandler.pSettings
                'mariadb
                pset(0).AppId = QGlobal.AppNames.MariaPortable
                pset(0).AppPath = QGlobal.AppDir & "MariaDb\bin\mysqld.exe"
                pset(0).UpgradeSignal = "mysql_upgrade"
                pset(0).UpgradeCmd = QGlobal.AppDir & "MariaDb\bin\mysql_upgrade.exe"
                pset(0).Cores = 0
                pset(0).Params = "--console"
                pset(0).WorkingDirectory = QGlobal.AppDir & "MariaDb\bin\"
                pset(0).StartSignal = "ready for connections"
                pset(0).StartsignalMaxTime = 60
                pset(1) = New clsProcessHandler.pSettings
                pset(1).AppId = QGlobal.AppNames.BRS
                If Q.settings.JavaType = QGlobal.AppNames.JavaInstalled Then
                    pset(1).AppPath = "java"
                Else
                    pset(1).AppPath = QGlobal.AppDir & "Java\bin\java.exe"
                End If
                pset(1).Cores = Q.settings.Cpulimit
                pset(1).Params = Q.settings.LaunchString()
                pset(1).StartSignal = "started successfully"
                pset(1).StartsignalMaxTime = 3600
                pset(1).WorkingDirectory = QGlobal.AppDir
                Q.ProcHandler.StartProcessSquence(pset)
            Else 'normal start
                Dim Pset As New clsProcessHandler.pSettings
                Pset.AppId = QGlobal.AppNames.BRS
                If Q.settings.JavaType = QGlobal.AppNames.JavaInstalled Then
                    Pset.AppPath = "java"
                Else
                    Pset.AppPath = QGlobal.AppDir & "Java\bin\java.exe"
                End If
                Pset.Cores = Q.settings.Cpulimit
                Pset.Params = Q.settings.LaunchString

                Pset.StartSignal = "started successfully"
                Pset.StartsignalMaxTime = 3600
                Pset.WorkingDirectory = QGlobal.AppDir
                Q.ProcHandler.StartProcess(Pset)
            End If

        End If

        'Update buttons and gui
        Running = True
        tsStartStop.Enabled = False
        btnStartStop.Enabled = False
        btnStartStop.Text = "Starting"
    End Sub

    Friend Sub StopWallet()

        StopAPIFetch()
        If Q.Service.IsInstalled Then
            Q.Service.StopService()
        Else
            If Q.settings.DbType = QGlobal.DbType.pMariaDB Then 'send startsequence
                Dim Pid(1) As Object
                Pid(0) = QGlobal.AppNames.BRS
                Pid(1) = QGlobal.AppNames.MariaPortable
                Q.ProcHandler.StopProcessSquence(Pid)
            Else
                Q.ProcHandler.StopProcess(QGlobal.AppNames.BRS)
            End If
        End If

        'Update buttons and gui
        lblGotoWallet.Visible = False
        btnStartStop.Text = "Stopping"
        btnStartStop.Enabled = False
        tsStartStop.Enabled = False
    End Sub

#End Region

#Region " Misc "

    Public Sub SetLoginMenu()
        mnuLoginAccount.DropDownItems.Clear()
        Dim mnuitm As ToolStripMenuItem
        For Each account As clsAccounts.Account In Q.Accounts.AccArray
            mnuitm = New ToolStripMenuItem
            mnuitm.Name = account.AccountName
            mnuitm.Text = account.AccountName
            AddHandler (mnuitm.Click), AddressOf LoadWallet
            mnuLoginAccount.DropDownItems.Add(mnuitm)
        Next
    End Sub

    Private Sub LoadWallet(sender As Object, e As EventArgs)
        Dim pwdf As New frmInput
        Dim mnuitm As ToolStripMenuItem = Nothing
        Try
            mnuitm = DirectCast(sender, ToolStripMenuItem)
        Catch ex As Exception
            Generic.WriteDebug(ex)
            Exit Sub
        End Try
        pwdf.Text = "Enter your pin"
        pwdf.lblInfo.Text = "Enter the pin for the account " & mnuitm.Text
        If pwdf.ShowDialog() = DialogResult.OK Then
            Dim pin As String = pwdf.txtPwd.Text
            If pin.Length > 5 Then
                Dim Pass As String = Q.Accounts.GetPassword(mnuitm.Name, pin)
                If Pass.Length > 0 Then
                    If Q.settings.QBMode = 0 And Q.settings.NoDirectLogin = False Then
                        Try
                            WB1.ExecuteJavascript("$('#remember_password').prop('checked', true);")

                            WB1.ExecuteJavascript("BRS.login('" & Pass & "');")

                            'Dim element As HtmlElement = wb1.Document.GetElementById("remember_password")

                            'If Not Convert.ToBoolean(element.GetAttribute("checked")) Then
                            ' element.InvokeMember("click")
                            ' End If

                            '    Dim codeString As String() = {[String].Format(" {0}('{1}') ", "BRS.login", Pass)}
                            'WB1.ExecuteJavascript("")

                            '  WB1.Document.InvokeScript("eval", codeString)
                            Pass = ""
                        Catch ex As Exception
                            Generic.WriteDebug(ex)
                        End Try
                    Else 'coppy to clipboard
                        MsgBox("Your passphrase is copied to clipoard. And will be erased after 30 seconds.",
                               MsgBoxStyle.Information Or MsgBoxStyle.OkOnly, "Clipboard")
                        My.Computer.Clipboard.SetText(Pass)
                        PasswordTimer.Interval = 30000
                        PasswordTimer.Enabled = True
                    End If
                Else
                    MsgBox("You entered the wrong pin.", MsgBoxStyle.Exclamation Or MsgBoxStyle.OkOnly, "Wrong pin")
                End If
            Else
                MsgBox("You entered the wrong pin.", MsgBoxStyle.Exclamation Or MsgBoxStyle.OkOnly, "Wrong pin")
            End If
        End If
    End Sub

    Public Sub SetDbInfo()

        Select Case Q.settings.DbType
            Case QGlobal.DbType.FireBird
                lblDbName.Text = Generic.GetDbNameFromType(QGlobal.DbType.FireBird)
                LblDbStatus.Text = "Embedded"
                LblDbStatus.ForeColor = Color.DarkGreen
            Case QGlobal.DbType.pMariaDB
                lblDbName.Text = Generic.GetDbNameFromType(QGlobal.DbType.pMariaDB)
                LblDbStatus.Text = "Stopped"
                LblDbStatus.ForeColor = Color.Red
            Case QGlobal.DbType.MariaDB
                lblDbName.Text = Generic.GetDbNameFromType(QGlobal.DbType.MariaDB)
                LblDbStatus.Text = "Unknown"
                LblDbStatus.ForeColor = Color.DarkOrange
            Case QGlobal.DbType.H2
                lblDbName.Text = Generic.GetDbNameFromType(QGlobal.DbType.H2)
                LblDbStatus.Text = "Embedded"
                LblDbStatus.ForeColor = Color.DarkGreen
        End Select
    End Sub

    Private Sub NewUpdatesAvilable()
        If InvokeRequired Then
            Dim d As New DNewUpdatesAvilable(AddressOf NewUpdatesAvilable)
            Invoke(d, New Object() {})
            Return
        End If
        Try
            lblUpdates.Visible = True
            lblUpdateAvail2.Visible = True
        Catch ex As Exception
            Generic.WriteDebug(ex)
        End Try
    End Sub

#End Region

#Region " Get Block Info "

    Public Sub StartAPIFetch()
        LastShowHeight = 0
        LastMinuteHeight = 0
        BlockMinute.Interval = 60000
        BlockMinute.Enabled = True
        BlockMinute.Start()

        APITimer.Interval = 1000
        APITimer.Enabled = True
        APITimer.Start()
    End Sub

    Public Sub StopAPIFetch()
        APITimer.Enabled = False
        APITimer.Stop()
        If lblBlockDate.Text.Contains("Downloading") Then
            lblBlockDate.Text = Mid(lblBlockDate.Text, 1, InStr(lblBlockDate.Text, "(") - 1)
        End If
        lblBlockDate.Text = Replace(lblBlockDate.Text, " (Fully Syncronized)", "")
    End Sub

    Private Sub APITimer_tick(sender As Object, e As EventArgs) Handles APITimer.Tick
        Dim trda As Thread
        trda = New Thread(AddressOf GetApiData)
        trda.IsBackground = True
        trda.Start()
        trda = Nothing
    End Sub

    Private Sub BlockMinute_tick(sender As Object, e As EventArgs) Handles BlockMinute.Tick

        LastShowHeight = CurHeight - LastMinuteHeight
        LastMinuteHeight = CurHeight
    End Sub


    Private Sub GetApiData()
        Try
            Dim http As New clsHttp
            Dim s() As String = Split(Q.settings.ListenIf, ";")
            Dim url As String = Nothing
            If s(0) = "0.0.0.0" Then
                url = "http://127.0.0.1:" & s(1)
            Else
                url = "http://" & s(0) & ":" & s(1)
            End If
            Dim Result() As String = Split(http.GetUrl(url & "/burst?requestType=getBlock"), ",")
            Dim Height = ""
            Dim TimeStamp = ""
            For Each Line As String In Result
                If Line.StartsWith(Chr(34) & "height" & Chr(34)) Then
                    Height = Line.Substring(9)
                End If
                If Line.StartsWith(Chr(34) & "timestamp" & Chr(34)) Then
                    TimeStamp = Replace(Line.Substring(12), "}", "")
                End If
                If Line.StartsWith(Chr(34) & "previousBlockHash" & Chr(34)) Then
                    Exit For
                End If
            Next
            APIResult(Height, TimeStamp)
        Catch ex As Exception
            Generic.WriteDebug(ex)
        End Try
    End Sub

    Private Sub APIResult(Data As String, TimeStamp As String)
        Try
            If InvokeRequired Then
                Dim d As New DAPIResult(AddressOf APIResult)
                Invoke(d, New Object() {Data, TimeStamp})
                Return
            End If
            lblBlockInfo.Text = Data '& " - " & CStr(LastShowHeight)
            CurHeight = CInt(Val(Data))
            Dim BlockDate As Date = TimeZoneInfo.ConvertTime(
                New DateTime(2014, 8, 11, 2, 0, 0).AddSeconds(Val(TimeStamp)), TimeZoneInfo.Utc, TimeZoneInfo.Local)
            If Now.AddHours(- 1) > BlockDate Then
                lblBlockDate.Text = BlockDate.ToString("yyyy-MM-dd HH:mm:ss") & " (Downloading blockchain at " &
                                    CStr(LastShowHeight) & " blocks/min)"
                lblBlockDate.ForeColor = Color.DarkOrange
                FullySynced = False
            Else
                lblBlockDate.Text = BlockDate.ToString("yyyy-MM-dd HH:mm:ss") & " (Fully Syncronized)"
                lblBlockDate.ForeColor = Color.DarkGreen
                FullySynced = True
            End If

        Catch ex As Exception
            Generic.WriteDebug(ex)
        End Try
    End Sub


    Private Sub StartWalletToolStripMenuItem_Click(sender As Object, e As EventArgs) Handles tsStartStop.Click
        StartStop()
    End Sub


    Private Sub AddAccountToolStripMenuItem_Click(sender As Object, e As EventArgs) _
        Handles AddAccountToolStripMenuItem.Click
        frmAccounts.Show()
        frmAccounts.Focus()
    End Sub


    Private Sub SetRewardassignmentToolStripMenuItem_Click(sender As Object, e As EventArgs) _
        Handles SetRewardassignmentToolStripMenuItem.Click
        frmSetrewardassignment.Show()
        frmSetrewardassignment.Focus()
    End Sub

    Private Sub MinerToolStripMenuItem_Click(sender As Object, e As EventArgs) Handles MinerToolStripMenuItem.Click
        frmMiner.Show()
        frmMiner.Focus()
    End Sub

    Private Sub ConfigureWindowsFirewallToolStripMenuItem1_Click(sender As Object, e As EventArgs) _
        Handles ConfigureWindowsFirewallToolStripMenuItem1.Click

        Dim msg As String =
                "Would you like to autmatically configure windows firewall with your wallet connection settings?" &
                vbCrLf
        msg &= "This will require Administrative priveleges."

        If MsgBox(msg, MsgBoxStyle.Information Or MsgBoxStyle.YesNo, "Windows firewall") = MsgBoxResult.Yes Then

            Generic.SetFirewallFromSettings()

        End If
    End Sub

    Private Sub ViewConsoleToolStripMenuItem_Click(sender As Object, e As EventArgs) _
        Handles ViewConsoleToolStripMenuItem.Click
        frmConsole.Show()
        frmConsole.Focus()
    End Sub


#End Region

    Private Sub OneMinCron_tick(sender As Object, e As EventArgs) Handles OneMinCron.Tick

        If Q.settings.DynPlotEnabled Then
            'check free space
            Dim Totalfree As Long
            Try
                Totalfree = Generic.GetDiskspace(Q.settings.DynPlotPath) _
                ' My.Computer.FileSystem.GetDriveInfo(Q.settings.DynPlotPath).TotalFreeSpace   'bytes
                Totalfree = CLng(Math.Floor(Totalfree/1024/1024/1024)) 'GiB

                If Totalfree > Q.settings.DynPlotFree + Q.settings.DynPlotSize Then _
'we must still have free space efter creation
                    'check if xplotter is running
                    If Not Generic.IsProcessRunning("xplotter") Then
                        'ok we can start a new plot
                        'find StartingNonce.
                        Dim Sn As String =
                                Generic.GetStartNonce(Q.settings.DynPlotAcc, (Q.settings.DynPlotSize*4096) - 1).ToString
                        Dim n As String = (Q.settings.DynPlotSize*4096).ToString
                        Dim p = New Process
                        p.StartInfo.WorkingDirectory = QGlobal.AppDir & "Xplotter"
                        Dim thepath As String = Q.settings.DynPlotPath
                        If thepath.Contains(" ") Then thepath = Chr(34) & thepath & Chr(34)
                        p.StartInfo.UseShellExecute = False

                        If Q.settings.DynPlotHide Then
                            p.StartInfo.CreateNoWindow = True
                        End If

                        Dim Arguments As String = "-id " & Q.settings.DynPlotAcc 'account id
                        Arguments &= " -sn " & Sn 'start nonce
                        Arguments &= " -n " & n ' amount of nonces
                        Arguments &= " -t " & Q.settings.DynThreads.ToString 'threadss
                        Arguments &= " -path " & thepath ' path
                        Arguments &= " -mem " & Q.settings.DynRam.ToString & "G" 'memory usage
                        If Q.settings.DynPlotType = 2 Then
                            Arguments &= " -poc2"
                        End If

                        p.StartInfo.Arguments = Arguments

                        If QGlobal.CPUInstructions.AVX2 Then
                            p.StartInfo.FileName = QGlobal.AppDir & "Xplotter\XPlotter_avx2.exe"
                        ElseIf QGlobal.CPUInstructions.AVX Then
                            p.StartInfo.FileName = QGlobal.AppDir & "Xplotter\XPlotter_avx.exe"
                        Else
                            p.StartInfo.FileName = QGlobal.AppDir & "Xplotter\XPlotter_sse.exe"
                        End If
                        p.Start()

                        Dim filePath As String = Q.settings.DynPlotPath
                        If Not filePath.EndsWith("\") Then filePath &= "\"
                        filePath &= Q.settings.DynPlotAcc & "_" 'account 
                        filePath &= Sn & "_" 'startnonce
                        filePath &= n  'length
                        If Q.settings.DynPlotType = 1 Then filePath &= "_" & n 'stagger
                        Q.settings.Plots &= filePath & "|"
                        Q.settings.SaveSettings()

                    End If
                End If
                'remove from files. we might fail if xplotter is running.
                If Totalfree < Q.settings.DynPlotFree Then
                    Generic.KillAllProcessesWithName("xplotter")
                    Dim Files() As String = Split(Q.settings.Plots, "|")
                    For t As Integer = UBound(Files) To 0 Step - 1
                        If LCase(Files(t)).StartsWith(LCase(Q.settings.DynPlotPath)) Then _
'is it in the dir with dynplotting?
                            'we have found a file to remove.
                            If File.Exists(Files(t)) Then
                                File.Delete(Files(t))
                                Q.settings.Plots = Replace(Q.settings.Plots, Files(t) & "|", "")
                                Q.settings.SaveSettings()
                                Exit For
                            Else
                                'not existing so we just remove it from path
                                Q.settings.Plots = Replace(Q.settings.Plots, Files(t) & "|", "")
                                Q.settings.SaveSettings()
                                'no exit we still need to remove.
                            End If
                        End If
                    Next
                End If
            Catch ex As Exception
                Generic.WriteDebug(ex)
            End Try
        End If

        If Q.settings.GetCoinMarket Then
            'coinmarket info
            Dim trda As Thread
            trda = New Thread(AddressOf FetchCoinMarket)
            trda.IsBackground = True
            trda.Start()
            trda = Nothing
        End If
    End Sub

    Private Sub FetchCoinMarket()
        Try
            Dim http As New clsHttp
            Dim result As String =
                    http.GetUrl("https://api.coinmarketcap.com/v1/ticker/burst/?convert=" & Q.settings.Currency)
            HttpResult(result)
        Catch ex As Exception

        End Try
    End Sub

    Private Sub HttpResult(Data As String)
        If InvokeRequired Then
            Dim d As New DHttpResult(AddressOf HttpResult)
            Invoke(d, New Object() {Data})
            Return
        End If
        Try
            Dim PriceBtc As Decimal = 0
            Dim PriceUSD As Decimal = 0
            Dim MktCap As Decimal = 0
            Dim buffer = ""
            Data = Replace(Data, Chr(34), "")
            Data = Replace(Data, vbLf, "")
            Data = Replace(Data, " ", "")
            Data = Replace(Data, "}", "")
            Data = Replace(Data, "]", "")
            Data = Replace(Data, "{", "")
            Data = Replace(Data, "[", "")
            Dim Entries() As String = Split(Data, ",")
            For t = 0 To UBound(Entries)

                If Entries(t).StartsWith("price_" & Q.settings.Currency.ToLower) Then
                    PriceUSD = Convert.ToDecimal(Mid(Entries(t), 11), CultureInfo.GetCultureInfo("en-US"))
                End If
                If Entries(t).StartsWith("price_btc") Then
                    PriceBtc = Convert.ToDecimal(Mid(Entries(t), 11), CultureInfo.GetCultureInfo("en-US"))
                End If
                If Entries(t).StartsWith("market_cap_" & Q.settings.Currency.ToLower) Then
                    MktCap = Convert.ToDecimal(Mid(Entries(t), 16), CultureInfo.GetCultureInfo("en-US"))
                End If
            Next
            lblCoinMarket.Text = "Burst price: " & PriceBtc.ToString & " btc | " & Q.settings.Currency & " " &
                                 Math.Round(PriceUSD, 3).ToString & " | Market cap : " & Q.settings.Currency & " " &
                                 Math.Round(MktCap/1000000, 2).ToString & "M"
        Catch ex As Exception
            lblCoinMarket.Text = "Burst price: N/A"
        End Try
    End Sub

    Private Sub RollbackChainpopoffToolStripMenuItem_Click(sender As Object, e As EventArgs) _
        Handles RollbackChainpopoffToolStripMenuItem.Click
        frmPopOff.Show()
        frmPopOff.Focus()
    End Sub

    Private Sub TrayIcon_MouseDoubleClick(sender As Object, e As MouseEventArgs) Handles TrayIcon.MouseClick
        WindowState = FormWindowState.Normal
        ShowInTaskbar = True
        TrayIcon.Visible = False
        Show()
    End Sub

    Private Sub WalletModeToolStripMenuItem1_Click(sender As Object, e As EventArgs) _
        Handles WalletModeToolStripMenuItem1.Click
        SetMode(0)
    End Sub

    Private Sub LauncherModeToolStripMenuItem1_Click(sender As Object, e As EventArgs) _
        Handles LauncherModeToolStripMenuItem1.Click
        SetMode(1)
    End Sub

    Private Sub cmbSelectWallet_Click(sender As Object, e As EventArgs) Handles cmbSelectWallet.SelectedIndexChanged
        Try
            If OneMinCron.Enabled = True Then
                Dim address = Q.AppManager.AppStore.Wallets(cmbSelectWallet.SelectedIndex).Address
                If cmbSelectWallet.SelectedIndex > 0 Then

                    WB1.LoadUrl(address)

                Else
                    If Running Then
                        WB1.LoadUrl(address & "?refreshToken=" + Guid.NewGuid().ToString())
                    Else
                        MsgBox("Your local wallet is not running. Checked address: " + address,
                               MsgBoxStyle.Information Or MsgBoxStyle.OkOnly, "Local wallet")
                    End If
                End If
            End If
        Catch ex As Exception

        End Try
    End Sub

    Private Sub VanityAddressGeneratorToolStripMenuItem_Click(sender As Object, e As EventArgs) _
        Handles VanityAddressGeneratorToolStripMenuItem.Click
        frmVanity.Show()
        frmVanity.Focus()
    End Sub

    Private Sub lblUpdateAvail2_Click(sender As Object, e As EventArgs) Handles lblUpdateAvail2.Click
        PrepareUpdate()
    End Sub


    Private Sub PlotconverterToolStripMenuItem1_Click(sender As Object, e As EventArgs) _
        Handles PlotconverterToolStripMenuItem1.Click
        If Not Q.AppManager.IsAppInstalled("PlotConverter") Then
            If _
                MsgBox("Plotconverter is not installed. Do you want to download and install it now?",
                       MsgBoxStyle.Information Or MsgBoxStyle.YesNo, "Download Plotconverter") = MsgBoxResult.Yes Then
                Hide()
                Dim res As Boolean = Q.AppManager.InstallApp("PlotConverter")
                Show()
                If res = False Then Exit Sub
            Else
                Exit Sub
            End If
        End If
        Try
            Dim p = New Process
            p.StartInfo.WorkingDirectory = QGlobal.AppDir & "PlotConverter"
            p.StartInfo.UseShellExecute = True
            p.StartInfo.FileName = QGlobal.AppDir & "PlotConverter\Poc1Poc2Conv.exe"
            p.Start()
        Catch ex As Exception

        End Try
    End Sub

    Private Sub PlotterToolStripMenuItem1_Click(sender As Object, e As EventArgs) _
        Handles PlotterToolStripMenuItem1.Click
        frmPlotter.Show()
        frmPlotter.Focus()
    End Sub

    Private Sub DynamicPlottingToolStripMenuItem1_Click(sender As Object, e As EventArgs) _
        Handles DynamicPlottingToolStripMenuItem1.Click
        frmDynamicPlotting.Show()
        frmDynamicPlotting.Focus()
    End Sub

    Private Sub QbundleManualToolStripMenuItem_Click(sender As Object, e As EventArgs) _
        Handles QbundleManualToolStripMenuItem.Click
        Process.Start("https://burstwiki.org/en/qbundle")
    End Sub

    Private Sub BurstcoinorgToolStripMenuItem1_Click(sender As Object, e As EventArgs) _
        Handles BurstcoinorgToolStripMenuItem1.Click
        Process.Start("https://www.burst-coin.org/")
    End Sub

    Private Sub BurstcoinistToolStripMenuItem1_Click(sender As Object, e As EventArgs) _
        Handles BurstcoinistToolStripMenuItem1.Click
        Process.Start("https://www.burstcoin.ist/")
    End Sub

    Private Sub BurstforumnetToolStripMenuItem_Click(sender As Object, e As EventArgs) _
        Handles BurstforumnetToolStripMenuItem.Click
        Process.Start("https://burstforum.net")
    End Sub

    Private Sub GetburstnetToolStripMenuItem_Click(sender As Object, e As EventArgs) _
        Handles GetburstnetToolStripMenuItem.Click
        Process.Start("https://forums.getburst.net/")
    End Sub

    Private Sub RedditBurstcoinToolStripMenuItem_Click(sender As Object, e As EventArgs) _
        Handles RedditBurstcoinToolStripMenuItem.Click
        Process.Start("https://www.reddit.com/r/burst/")
    End Sub

    Private Sub BurstWikiToolStripMenuItem_Click(sender As Object, e As EventArgs) _
        Handles BurstWikiToolStripMenuItem.Click
        Process.Start("https://burstwiki.org/")
    End Sub

    Private Sub PaperburstToolStripMenuItem_Click(sender As Object, e As EventArgs) _
        Handles PaperburstToolStripMenuItem.Click
        If Not Q.AppManager.IsAppInstalled("Paperburst") Then
            If _
                MsgBox("PaperBurst is not installed. Do you want to download and install it now?",
                       MsgBoxStyle.Information Or MsgBoxStyle.YesNo, "Download Plotconverter") = MsgBoxResult.Yes Then
                Hide()
                Dim res As Boolean = Q.AppManager.InstallApp("Paperburst")
                Show()
                If res = False Then Exit Sub
            Else
                Exit Sub
            End If
        End If
        Try
            Dim p = New Process
            p.StartInfo.WorkingDirectory = QGlobal.AppDir & "PaperBurst"
            p.StartInfo.UseShellExecute = True
            p.StartInfo.FileName = QGlobal.AppDir & "PaperBurst\PaperBurst.exe"
            p.Start()
        Catch ex As Exception

        End Try
    End Sub


    Private Function CheckEnvironment()

        If Not Q.AppManager.IsAppInstalled("BRS") Then
            Return False
        End If
        'making sure we have java
        If Not Q.AppManager.isJavaInstalled Then
            Return False
        End If


        Return True
    End Function
End Class