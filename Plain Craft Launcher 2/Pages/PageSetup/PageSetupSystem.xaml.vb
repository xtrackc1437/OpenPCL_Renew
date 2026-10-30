Public Class PageSetupSystem


    Private Sub PageSetupSystem_Loaded(sender As Object, e As RoutedEventArgs) Handles Me.Loaded

        '重复加载部分
        PanBack.ScrollToHome()
        RefreshUpdateState()

        '非重复加载部分
        Static IsLoaded As Boolean = False
        If IsLoaded Then Return
        IsLoaded = True

        AniControlEnabled += 1
        Reload()
        SliderLoad()
        AniControlEnabled -= 1

        AddHandler UpdateDownloadLoader.OnStateChangedUi, AddressOf UpdateDownloadLoader_StateChanged

    End Sub
    Public Sub Reload()
        SettingService.RefreshSettings(Me)
    End Sub
    Public Sub Reset()
        Try
            SettingService.ResetSettings(Me)
            Logger.Info("已初始化启动器页设置")
            Hint("已初始化启动器页设置！", HintType.Green, False)
        Catch ex As Exception
            Logger.Error(ex, "初始化启动器页设置失败", LogBehavior.Alert)
        End Try
        Reload()
    End Sub

    '滑动条
    Private Sub SliderLoad()
        SliderDownloadThread.GetHintText = Function(v) v + 1
        SliderDownloadSpeed.GetHintText =
        Function(v)
            Select Case v
                Case Is <= 14
                    Return (v + 1) * 0.1 & " M/s"
                Case Is <= 31
                    Return (v - 11) * 0.5 & " M/s"
                Case Is <= 41
                    Return (v - 21) & " M/s"
                Case Else
                    Return "无限制"
            End Select
        End Function
        SliderDebugAnim.GetHintText = Function(v) If(v > 29, "关闭", (v / 10 + 0.1) & "x")
    End Sub
    Private Sub SliderDownloadThread_PreviewChange(sender As Object, e As RouteEventArgs) Handles SliderDownloadThread.PreviewChange
        If SliderDownloadThread.Value < 100 Then Return
        If Not Settings.Get(Of Boolean)("HintDownloadThread") Then
            Settings.Set("HintDownloadThread", True)
            MyMsgBox("如果设置过多的下载线程，可能会导致下载时出现非常严重的卡顿。" & vbCrLf &
                     "一般设置 64 线程即可满足大多数下载需求，除非你知道你在干什么，否则不建议设置更多的线程数！", "警告", "我知道了", IsWarn:=True)
        End If
    End Sub

    '调试模式
    Private Sub CheckDebugMode_Change() Handles CheckDebugMode.Change
        If AniControlEnabled = 0 Then Hint("部分调试信息将在刷新或启动器重启后切换显示！",, False)
    End Sub

    ''' <summary>
    ''' 启动器是否已经是最新版？
    ''' 若返回 Nothing，则代表尚无更新检测结果或出错。
    ''' </summary>
    Public Shared Function IsLauncherNewest() As Boolean?
        Return GetCachedIsNewest()
    End Function

#Region "启动器更新"

    Private Sub RefreshUpdateState()
        LabUpdateVersion.Text = $"当前版本 {VersionBaseName}（{BuildTypeDisplay}）"
        If UpdateDownloadLoader.State = LoadState.Loading Then
            LoadUpdate.State = UpdateDownloadLoader
            LoadUpdate.Visibility = Visibility.Visible
        Else
            LoadUpdate.Visibility = Visibility.Collapsed
        End If
        '若用户在其它页面选择了立即更新，则在这里开始显示进度
        Dim Pending = PendingUpdate
        If Pending IsNot Nothing Then
            PendingUpdate = Nothing
            StartUpdateDownload(Pending)
        End If
    End Sub
    Private Sub UpdateDownloadLoader_StateChanged(Loader As LoaderBase, NewState As LoadState, OldState As LoadState)
        Select Case NewState
            Case LoadState.Loading
                LoadUpdate.State = Loader
                LoadUpdate.Visibility = Visibility.Visible
            Case LoadState.Finished
                LoadUpdate.Visibility = Visibility.Collapsed
                If MyMsgBox("更新已下载完成，启动器将重启以完成更新。", "更新完成", "立即重启", "稍后") = 1 Then
                    Try
                        UpdateApplyAndRestart()
                    Catch ex As Exception
                        Logger.Error(ex, "应用更新失败")
                        ShowUpdateFailed(ex.Message)
                    End Try
                Else
                    '退出时再替换文件，下次启动即为新版本
                    IsUpdateWaitingRestart = True
                    Hint("已下载的更新将在启动器退出时安装", HintType.Green)
                End If
            Case LoadState.Failed
                LoadUpdate.Visibility = Visibility.Collapsed
                ShowUpdateFailed(Loader.Error?.Message)
        End Select
    End Sub
    Private Sub ShowUpdateFailed(Message As String)
        MyMsgBox($"下载或安装更新失败：{vbCrLf}{Message}{vbCrLf}{vbCrLf}你也可以手动下载最新版本并替换当前程序。",
                 "更新失败", "打开 Releases", "取消", IsWarn:=True,
                 Button1Action:=Sub() OpenWebsite(UpdateReleasesPage))
    End Sub
    Private Sub BtnUpdateCheck_Click(sender As Object, e As MouseButtonEventArgs) Handles BtnUpdateCheck.Click
        UpdateCheckByButton()
    End Sub
    Private Sub BtnUpdatePage_Click(sender As Object, e As MouseButtonEventArgs) Handles BtnUpdatePage.Click
        OpenWebsite(UpdateReleasesPage)
    End Sub

#End Region

#Region "导出 / 导入设置"

    Private Sub BtnSystemSettingExp_Click(sender As Object, e As MouseButtonEventArgs) Handles BtnSystemSettingExp.Click
        Hint("该功能尚在开发中！")
    End Sub
    Private Sub BtnSystemSettingImp_Click(sender As Object, e As MouseButtonEventArgs) Handles BtnSystemSettingImp.Click
        Hint("该功能尚在开发中！")
    End Sub

#End Region

End Class
