using NopeNote.Windows.Helpers;
using System;

namespace NopeNote.Windows.Services;

/// <summary>
/// 进程内的组装点：设置、搜索索引和仓库服务各一份，在启动时创建并向下传递。
/// 页面和窗口不再自行取静态单例。
/// </summary>
public sealed class AppServices
{
    public AppServices(SettingsHelper settings, NoteSearchIndex searchIndex, VaultService vault)
    {
        Settings = settings;
        SearchIndex = searchIndex;
        Vault = vault;
    }

    public SettingsHelper Settings { get; }

    public NoteSearchIndex SearchIndex { get; }

    public VaultService Vault { get; }

    public static AppServices CreateDefault()
    {
        SettingsHelper settings = new();
        return new AppServices(settings, new NoteSearchIndex(), new VaultService(settings));
    }
}

/// <summary>笔记页的导航参数：可选的待打开笔记，加上共享的服务容器。</summary>
public sealed record NotesNavigation(Guid? NoteId, AppServices Services);
