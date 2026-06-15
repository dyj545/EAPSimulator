using CommunityToolkit.Mvvm.ComponentModel;

namespace EAPSimulator.Wpf.ViewModels;

public partial class MainViewModel : ObservableObject
{
    public ConfigViewModel Config { get; } = new();
    public MessageLogViewModel MessageLog { get; } = new();
    public StatusPanelViewModel StatusPanel { get; } = new();
    public MessageEditorViewModel MessageEditor { get; } = new();
    public AutoReplyViewModel AutoReply { get; } = new();

    [ObservableProperty] private string _statusMessage = "就绪";

    public MainViewModel()
    {
        LoadDefaultData();
    }

    private void LoadDefaultData()
    {
        try
        {
            var templatesPath = System.IO.Path.Combine(
                AppDomain.CurrentDomain.BaseDirectory, "secs_message_templates.json");
            if (System.IO.File.Exists(templatesPath))
                MessageEditor.LoadFromFile(templatesPath);

            var configPath = System.IO.Path.Combine(
                AppDomain.CurrentDomain.BaseDirectory, "auto_reply_rules.json");
            if (System.IO.File.Exists(configPath))
                AutoReply.LoadConfig(configPath);

            StatusMessage = $"已加载 {MessageEditor.AllMessages.Count} 条消息模板";
        }
        catch (Exception ex)
        {
            StatusMessage = $"加载失败: {ex.Message}";
        }
    }
}
