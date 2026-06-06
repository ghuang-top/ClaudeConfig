using System.Diagnostics;
using System.IO;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Windows;

namespace ClaudeDeepSeekConfigurator;

public partial class MainWindow : Window
{
    // deepseek-config.json 与可执行程序放在同一目录
    private static readonly string ConfigPath =
        Path.Combine(AppContext.BaseDirectory, "deepseek-config.json");

    // Claude Code 用户配置：%USERPROFILE%\.claude\settings.json
    private static readonly string ClaudeSettingsPath =
        Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            ".claude", "settings.json");

    public MainWindow()
    {
        InitializeComponent();
        LoadConfig();
    }

    // 当前 key 文本（同步明文/密文两个控件）
    private string KeyText
    {
        get => KeyPlain.Visibility == Visibility.Visible ? KeyPlain.Text : KeyPassword.Password;
        set
        {
            KeyPassword.Password = value;
            KeyPlain.Text = value;
        }
    }

    private void ToggleKeyButton_Click(object sender, RoutedEventArgs e)
    {
        if (KeyPlain.Visibility == Visibility.Visible)
        {
            KeyPassword.Password = KeyPlain.Text;
            KeyPlain.Visibility = Visibility.Collapsed;
            KeyPassword.Visibility = Visibility.Visible;
            ToggleKeyButton.Content = "显示";
        }
        else
        {
            KeyPlain.Text = KeyPassword.Password;
            KeyPassword.Visibility = Visibility.Collapsed;
            KeyPlain.Visibility = Visibility.Visible;
            ToggleKeyButton.Content = "隐藏";
        }
    }

    private void Log(string message)
    {
        if (LogBox.Dispatcher.CheckAccess())
        {
            LogBox.AppendText($"[{DateTime.Now:HH:mm:ss}] {message}{Environment.NewLine}");
            LogBox.ScrollToEnd();
        }
        else
        {
            LogBox.Dispatcher.BeginInvoke(new Action<string>(Log), message);
        }
    }

    private void LoadConfig()
    {
        if (!File.Exists(ConfigPath))
        {
            Log($"未找到配置文件，将使用空白值。保存后会创建：{ConfigPath}");
            return;
        }

        try
        {
            var json = File.ReadAllText(ConfigPath, Encoding.UTF8);
            var node = JsonNode.Parse(json)?.AsObject();
            if (node is null)
            {
                Log("配置文件为空或格式不正确。");
                return;
            }

            UrlBox.Text = node["url"]?.GetValue<string>() ?? string.Empty;
            KeyText = node["key"]?.GetValue<string>() ?? string.Empty;
            OpusBox.Text = node["opus_model"]?.GetValue<string>() ?? string.Empty;
            SonnetBox.Text = node["sonnet_model"]?.GetValue<string>() ?? string.Empty;
            HaikuBox.Text = node["haiku_model"]?.GetValue<string>() ?? string.Empty;

            Log($"已加载配置：{ConfigPath}");
        }
        catch (Exception ex)
        {
            Log($"配置文件解析失败：{ex.Message}");
        }
    }

    private (string url, string key, string opus, string sonnet, string haiku) ReadInputs()
    {
        return (
            UrlBox.Text.Trim(),
            KeyText.Trim(),
            OpusBox.Text.Trim(),
            SonnetBox.Text.Trim(),
            HaikuBox.Text.Trim());
    }

    private bool ValidateInputs(out (string url, string key, string opus, string sonnet, string haiku) values)
    {
        values = ReadInputs();
        if (string.IsNullOrWhiteSpace(values.url) ||
            string.IsNullOrWhiteSpace(values.key) ||
            string.IsNullOrWhiteSpace(values.opus) ||
            string.IsNullOrWhiteSpace(values.sonnet) ||
            string.IsNullOrWhiteSpace(values.haiku))
        {
            MessageBox.Show("url / key / opus_model / sonnet_model / haiku_model 均不能为空。",
                "校验失败", MessageBoxButton.OK, MessageBoxImage.Warning);
            return false;
        }

        if (values.key.Contains("填入"))
        {
            MessageBox.Show("请先把 key 替换为你的真实 API Key。",
                "校验失败", MessageBoxButton.OK, MessageBoxImage.Warning);
            return false;
        }

        return true;
    }

    private void SaveButton_Click(object sender, RoutedEventArgs e)
    {
        var v = ReadInputs();
        try
        {
            var obj = new JsonObject
            {
                ["url"] = v.url,
                ["key"] = v.key,
                ["opus_model"] = v.opus,
                ["sonnet_model"] = v.sonnet,
                ["haiku_model"] = v.haiku
            };

            var options = new JsonSerializerOptions
            {
                WriteIndented = true,
                Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping
            };
            File.WriteAllText(ConfigPath, obj.ToJsonString(options), new UTF8Encoding(false));
            Log($"配置已保存：{ConfigPath}");
        }
        catch (Exception ex)
        {
            Log($"保存失败：{ex.Message}");
            MessageBox.Show(ex.Message, "保存失败", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void ApplyButton_Click(object sender, RoutedEventArgs e)
    {
        if (!ValidateInputs(out var v))
            return;

        try
        {
            var envVars = new Dictionary<string, string>
            {
                ["ANTHROPIC_BASE_URL"] = v.url,
                ["ANTHROPIC_AUTH_TOKEN"] = v.key,
                ["ANTHROPIC_DEFAULT_OPUS_MODEL"] = v.opus,
                ["ANTHROPIC_DEFAULT_OPUS_MODEL_NAME"] = v.opus,
                ["ANTHROPIC_DEFAULT_OPUS_MODEL_DESCRIPTION"] = $"{v.opus} via Anthropic-compatible endpoint",
                ["ANTHROPIC_DEFAULT_SONNET_MODEL"] = v.sonnet,
                ["ANTHROPIC_DEFAULT_SONNET_MODEL_NAME"] = v.sonnet,
                ["ANTHROPIC_DEFAULT_SONNET_MODEL_DESCRIPTION"] = $"{v.sonnet} via Anthropic-compatible endpoint",
                ["ANTHROPIC_DEFAULT_HAIKU_MODEL"] = v.haiku,
                ["ANTHROPIC_DEFAULT_HAIKU_MODEL_NAME"] = v.haiku,
                ["ANTHROPIC_DEFAULT_HAIKU_MODEL_DESCRIPTION"] = $"{v.haiku} via Anthropic-compatible endpoint"
            };

            // 不再需要的旧变量（可能在之前的运行中持久化）
            var obsoleteVars = new[]
            {
                "ANTHROPIC_MODEL",
                "CLAUDE_CODE_SUBAGENT_MODEL",
                "CLAUDE_CODE_EFFORT_LEVEL"
            };

            Task.Run(() => 
            {
                Log("正在写入环境变量（当前进程 + 用户级持久化）...");
                foreach (var (name, value) in envVars)
                {
                    Environment.SetEnvironmentVariable(name, value, EnvironmentVariableTarget.Process);
                    Environment.SetEnvironmentVariable(name, value, EnvironmentVariableTarget.User);
                    var shown = name == "ANTHROPIC_AUTH_TOKEN" ? "******（已设置）" : value;
                    Log($"  {name} = {shown}");
                }

                foreach (var name in obsoleteVars)
                {
                    Environment.SetEnvironmentVariable(name, null, EnvironmentVariableTarget.Process);
                    Environment.SetEnvironmentVariable(name, null, EnvironmentVariableTarget.User);
                    Log($"  已清理旧变量：{name}");
                }

                Log("环境变量配置完成。");

                WriteClaudeSettings(envVars);
                Log($"Claude Code 用户配置已更新：{ClaudeSettingsPath}");

                Log("配置已应用。新开的终端 / Claude Code 会读取到最新设置。");

                //MessageBox.Show("配置已应用。新开的终端 / Claude Code 会读取到最新设置。",
                //    "完成", MessageBoxButton.OK, MessageBoxImage.Information);
            });
        }
        catch (Exception ex)
        {
            Log($"应用失败：{ex.Message}");
            MessageBox.Show(ex.Message, "应用失败", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private static void WriteClaudeSettings(Dictionary<string, string> envVars)
    {
        var dir = Path.GetDirectoryName(ClaudeSettingsPath)!;
        Directory.CreateDirectory(dir);

        JsonObject settings;
        if (File.Exists(ClaudeSettingsPath))
        {
            var existing = File.ReadAllText(ClaudeSettingsPath, Encoding.UTF8);
            settings = string.IsNullOrWhiteSpace(existing)
                ? new JsonObject()
                : JsonNode.Parse(existing)?.AsObject() ?? new JsonObject();
        }
        else
        {
            settings = new JsonObject();
        }

        if (settings["env"] is not JsonObject envObj)
        {
            envObj = new JsonObject();
            settings["env"] = envObj;
        }

        foreach (var (name, value) in envVars)
        {
            envObj[name] = value;
        }

        // 默认从 Opus 槽启动；真实模型由 ANTHROPIC_DEFAULT_OPUS_MODEL 映射。
        settings["model"] = "opus";

        var options = new JsonSerializerOptions
        {
            WriteIndented = true,
            Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping
        };
        File.WriteAllText(ClaudeSettingsPath, settings.ToJsonString(options), new UTF8Encoding(false));
    }

    private void LaunchButton_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            // 在新的控制台窗口里启动 claude，便于交互
            var psi = new ProcessStartInfo
            {
                FileName = "cmd.exe",
                Arguments = "/k claude",
                UseShellExecute = true
            };
            Process.Start(psi);
            Log("已尝试启动 claude（若提示找不到命令，请先执行：npm install -g @anthropic-ai/claude-code）。");
        }
        catch (Exception ex)
        {
            Log($"启动失败：{ex.Message}");
            MessageBox.Show(ex.Message, "启动失败", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }
}
