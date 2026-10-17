using System.Drawing;
using NetcShared;

namespace NetcServer.Agent;

/// <summary>Всплывающее сообщение учителя в сессии ученика (с возможностью ответа).</summary>
public sealed class MessageForm : Form
{
    private readonly Func<ChatMessage, Task>? _reply;
    private readonly TextBox? _input;

    public MessageForm(ChatMessage msg, Func<ChatMessage, Task>? reply)
    {
        _reply = reply;

        Text = "Сообщение от учителя";
        AutoScaleMode = AutoScaleMode.Font;
        StartPosition = FormStartPosition.CenterScreen;
        TopMost = true;
        MinimizeBox = false;
        MaximizeBox = false;
        ShowInTaskbar = true;

        bool canReply = reply is not null && msg.ExpectReply;
        ClientSize = new Size(560, canReply ? 400 : 340);

        var text = new TextBox
        {
            Multiline = true,
            ReadOnly = true,
            ScrollBars = ScrollBars.Vertical,
            Font = new Font("Segoe UI", 12F),
            Bounds = new Rectangle(12, 12, 536, canReply ? 250 : 286),
            Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right
        };
        text.Text = msg.Text;
        Controls.Add(text);

        Button? replyButton = null;
        if (canReply)
        {
            _input = new TextBox
            {
                Multiline = true,
                ScrollBars = ScrollBars.Vertical,
                Font = new Font("Segoe UI", 10F),
                Bounds = new Rectangle(12, 272, 536, 70),
                Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right
            };
            Controls.Add(_input);

            replyButton = new Button
            {
                Text = "Ответить",
                Bounds = new Rectangle(12, 352, 120, 32),
                Anchor = AnchorStyles.Bottom | AnchorStyles.Left
            };
            replyButton.Click += async (_, _) => await SendReplyAsync();
            Controls.Add(replyButton);
        }

        var btnClose = new Button
        {
            Text = "Закрыть",
            Bounds = new Rectangle(ClientSize.Width - 132, ClientSize.Height - 44, 120, 32),
            Anchor = AnchorStyles.Bottom | AnchorStyles.Right
        };
        btnClose.Click += (_, _) => Close();
        Controls.Add(btnClose);

        AcceptButton = (IButtonControl?)replyButton ?? btnClose;
    }

    private async Task SendReplyAsync()
    {
        if (_input is null) return;
        string t = _input.Text.Trim();
        if (t.Length == 0) return;

        var reply = new ChatMessage { From = Environment.UserName, Text = t };
        try
        {
            if (_reply is not null) await _reply(reply);
        }
        catch { }

        _input.Clear();
        _input.Focus();
    }
}
