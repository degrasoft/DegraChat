using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using DegraChat.Core.Models;
using Scriban;
using Scriban.Runtime;
using Serilog;

namespace DegraChat.Overlay.Engine;

/// <summary>
/// Generates the final HTML overlay file from templates and user style settings.
/// Uses Scriban for template rendering with CSS variable substitution.
/// </summary>
public class OverlayGenerator
{
    private readonly ILogger _logger;
    private readonly string _templatesDirectory;

    public OverlayGenerator(ILogger logger, string? templatesDirectory = null)
    {
        _logger = logger.ForContext<OverlayGenerator>();
        _templatesDirectory = templatesDirectory ?? FindTemplatesDirectory();
    }

    /// <summary>
    /// Generate the final overlay HTML file with embedded CSS and JS.
    /// </summary>
    public async Task<string> GenerateOverlayAsync(OverlayStyle style, ServerConfig serverConfig, CancellationToken cancellationToken = default)
    {
        _logger.Information("Generating overlay with style profile: {Profile}", style.ProfileName);

        var cssVariables = GenerateCssVariables(style);
        var customCss = style.CustomCss ?? string.Empty;

        var templatePath = Path.Combine(_templatesDirectory, "overlay.html.scriban");
        string templateContent;

        if (File.Exists(templatePath))
        {
            templateContent = await File.ReadAllTextAsync(templatePath, cancellationToken);
        }
        else
        {
            _logger.Warning("Template file not found at {Path}, using built-in template", templatePath);
            templateContent = GetBuiltInTemplate();
        }

        var template = Template.Parse(templateContent);
        if (template.HasErrors)
        {
            var errors = string.Join("\n", template.Messages.Select(m => m.ToString()));
            throw new InvalidOperationException($"Template parse errors:\n{errors}");
        }

        var scriptObject = new ScriptObject
        {
            ["cssVariables"] = cssVariables,
            ["customCss"] = customCss,
            ["wsUrl"] = $"ws://{serverConfig.Host}:{serverConfig.Port}/ws",
            ["style"] = new ScriptObject
            {
                ["maxMessages"] = style.MaxMessages,
                ["messageDirection"] = style.MessageDirection,
                ["animationIn"] = style.AnimationIn,
                ["animationOut"] = style.AnimationOut,
                ["animationDurationMs"] = style.AnimationDurationMs,
                ["messageDisplayTimeMs"] = style.MessageDisplayTimeMs,
                ["showPlatformIcon"] = style.ShowPlatformIcon,
                ["showSeparator"] = style.ShowSeparator,
                ["showShadow"] = style.ShowShadow,
                ["profileName"] = style.ProfileName
            }
        };

        var context = new TemplateContext();
        context.PushGlobal(scriptObject);

        var result = await Task.Run(() => template.Render(context), cancellationToken);

        _logger.Information("Overlay generated successfully ({Length} characters)", result.Length);
        return result;
    }

    /// <summary>
    /// Save the generated overlay to a file.
    /// </summary>
    public async Task SaveOverlayAsync(string html, string outputPath, CancellationToken cancellationToken = default)
    {
        var dir = Path.GetDirectoryName(outputPath);
        if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
        {
            Directory.CreateDirectory(dir);
        }

        await File.WriteAllTextAsync(outputPath, html, Encoding.UTF8, cancellationToken);
        _logger.Information("Overlay saved to {Path}", outputPath);
    }

    /// <summary>
    /// Generate CSS variables string from OverlayStyle.
    /// </summary>
    public static string GenerateCssVariables(OverlayStyle style)
    {
        var sb = new StringBuilder();
        sb.AppendLine(":root {");
        sb.AppendLine($"  --cos-background-color: {style.BackgroundColor};");
        sb.AppendLine($"  --cos-background-opacity: {style.BackgroundOpacity};");
        sb.AppendLine($"  --cos-border-radius: {style.BorderRadius}px;");
        sb.AppendLine($"  --cos-padding: {style.Padding}px;");
        sb.AppendLine($"  --cos-message-font-family: '{style.MessageFontFamily}', sans-serif;");
        sb.AppendLine($"  --cos-message-font-size: {style.MessageFontSize}px;");
        sb.AppendLine($"  --cos-message-color: {style.MessageColor};");
        sb.AppendLine($"  --cos-message-line-height: {style.MessageLineHeight};");
        sb.AppendLine($"  --cos-username-font-family: '{style.UsernameFontFamily}', sans-serif;");
        sb.AppendLine($"  --cos-username-font-size: {style.UsernameFontSize}px;");
        sb.AppendLine($"  --cos-username-bold: {(style.UsernameBold ? "bold" : "normal")};");
        sb.AppendLine($"  --cos-badge-size: {style.BadgeSize}px;");
        sb.AppendLine($"  --cos-emote-size: {style.EmoteSize}px;");
        sb.AppendLine($"  --cos-message-spacing: {style.MessageSpacing}px;");
        sb.AppendLine($"  --cos-animation-duration: {style.AnimationDurationMs}ms;");
        sb.AppendLine($"  --cos-separator-color: {style.SeparatorColor};");
        sb.AppendLine($"  --cos-shadow-color: {style.ShadowColor};");
        sb.AppendLine($"  --cos-shadow-blur: {style.ShadowBlur}px;");
        sb.AppendLine($"  --cos-shadow-offset-x: {style.ShadowOffsetX}px;");
        sb.AppendLine($"  --cos-shadow-offset-y: {style.ShadowOffsetY}px;");
        sb.AppendLine("}");
        return sb.ToString();
    }

    private static string FindTemplatesDirectory()
    {
        // Look for templates relative to the assembly location
        var assemblyDir = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location);
        if (assemblyDir != null)
        {
            var templatesPath = Path.Combine(assemblyDir, "Templates");
            if (Directory.Exists(templatesPath))
                return templatesPath;
        }

        // Development path
        var currentDir = Directory.GetCurrentDirectory();
        var devTemplates = Path.Combine(currentDir, "Templates");
        if (Directory.Exists(devTemplates))
            return devTemplates;

        return Path.Combine(currentDir, "Templates");
    }

    /// <summary>
    /// Built-in fallback template when no external template file is found.
    /// </summary>
    private static string GetBuiltInTemplate() => """
        <!DOCTYPE html>
        <html lang="en">
        <head>
            <meta charset="UTF-8">
            <meta name="viewport" content="width=device-width, initial-scale=1.0">
            <title>ChatOverlay Studio - {{ style.profileName }}</title>
            <style>
                {{ css_variables }}

                * { margin: 0; padding: 0; box-sizing: border-box; }
                body { background: transparent; overflow: hidden; }

                #chat-container {
                    display: flex;
                    flex-direction: {{ style.message_direction }};
                    padding: var(--cos-padding);
                    gap: var(--cos-message-spacing);
                    max-height: 100vh;
                    overflow: hidden;
                }

                .chat-message {
                    display: flex;
                    align-items: flex-start;
                    gap: 8px;
                    padding: 8px 12px;
                    border-radius: var(--cos-border-radius);
                    background: rgba(var(--cos-background-color), var(--cos-background-opacity));
                    {{ if style.show_shadow }}
                    box-shadow: var(--cos-shadow-offset-x) var(--cos-shadow-offset-y) var(--cos-shadow-blur) var(--cos-shadow-color);
                    {{ end }}
                    word-break: break-word;
                    animation: messageIn var(--cos-animation-duration) ease;
                }

                .chat-message .platform-icon { width: 18px; height: 18px; flex-shrink: 0; }
                .chat-message .badges { display: flex; gap: 2px; align-items: center; }
                .chat-message .badges img { width: var(--cos-badge-size); height: var(--cos-badge-size); }
                .chat-message .username {
                    font-family: var(--cos-username-font-family);
                    font-size: var(--cos-username-font-size);
                    font-weight: var(--cos-username-bold);
                    margin-right: 6px;
                    white-space: nowrap;
                }
                .chat-message .text {
                    font-family: var(--cos-message-font-family);
                    font-size: var(--cos-message-font-size);
                    color: var(--cos-message-color);
                    line-height: var(--cos-message-line-height);
                }
                .chat-message .emote { width: var(--cos-emote-size); height: var(--cos-emote-size); vertical-align: middle; }
                .chat-message.system { color: #FF6600; font-style: italic; }

                {{ if style.show_separator }}
                .chat-message + .chat-message { border-top: 1px solid var(--cos-separator-color); }
                {{ end }}

                @keyframes messageIn {
                    from { opacity: 0; transform: translateY(10px); }
                    to { opacity: 1; transform: translateY(0); }
                }
                @keyframes messageOut {
                    from { opacity: 1; }
                    to { opacity: 0; }
                }

                {{ custom_css }}
            </style>
        </head>
        <body>
            <div id="chat-container"></div>
            <script>
                const container = document.getElementById('chat-container');
                const maxMessages = {{ style.max_messages }};
                const displayTimeMs = {{ style.message_display_time_ms }};
                const wsUrl = '{{ ws_url }}';
                let ws;

                function connect() {
                    ws = new WebSocket(wsUrl);
                    ws.onopen = () => console.log('ChatOverlay Studio: Connected');
                    ws.onmessage = (event) => {
                        try {
                            const msg = JSON.parse(event.data);
                            if (msg.type === 'message') addMessage(msg.data);
                            else if (msg.type === 'clear') container.innerHTML = '';
                        } catch(e) { console.error(e); }
                    };
                    ws.onclose = () => setTimeout(connect, 3000);
                    ws.onerror = () => {};
                }

                function addMessage(data) {
                    const el = document.createElement('div');
                    el.className = 'chat-message' + (data.isSystem ? ' system' : '');
                    
                    let html = '';
                    if (data.badges && data.badges.length) {
                        html += '<span class="badges">' + data.badges.map(b => '<img src="'+b+'" alt="">').join('') + '</span>';
                    }
                    html += '<span class="username" style="color:'+(data.userColor||'#FFF')+'">'+esc(data.displayName||'')+'</span>';
                    html += '<span class="text">'+processEmotes(data.text||'', data.emotes||{})+'</span>';
                    el.innerHTML = html;
                    
                    if (container.firstChild) container.insertBefore(el, container.firstChild);
                    else container.appendChild(el);
                    
                    while (container.children.length > maxMessages) container.removeChild(container.lastChild);
                    
                    if (displayTimeMs > 0) {
                        setTimeout(() => {
                            el.style.animation = 'messageOut var(--cos-animation-duration) ease forwards';
                            setTimeout(() => el.remove(), {{ style.animation_duration_ms }});
                        }, displayTimeMs);
                    }
                }

                function processEmotes(text, emotes) {
                    if (!emotes || !Object.keys(emotes).length) return esc(text);
                    let r = esc(text);
                    for (const [n,u] of Object.entries(emotes)) {
                        r = r.replace(new RegExp(esc(n).replace(/[.*+?^${}()|[\]\\]/g,'\\$&'),'g'), '<img class="emote" src="'+u+'" alt="'+esc(n)+'">');
                    }
                    return r;
                }

                function esc(s) { const d=document.createElement('div'); d.textContent=s; return d.innerHTML; }
                connect();
            </script>
        </body>
        </html>
        """;
}
