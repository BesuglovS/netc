using System;
using System.Drawing;
using System.Windows.Forms;

namespace ControlWorksCollector
{
    /// <summary>Небольшой диалог ввода пароля (не показывается при сохранённом пароле).</summary>
    public sealed class PasswordPrompt : Form
    {
        private readonly TextBox _txt;

        public string Password
        {
            get { return _txt.Text; }
        }

        public PasswordPrompt(string user, string hint)
        {
            AutoScaleMode = AutoScaleMode.Font;
            AutoScaleDimensions = new SizeF(7F, 15F);
            Text = "Пароль администратора";
            FormBorderStyle = FormBorderStyle.FixedDialog;
            StartPosition = FormStartPosition.CenterParent;
            MaximizeBox = false;
            MinimizeBox = false;
            ClientSize = new Size(430, 175);

            TableLayoutPanel root = new TableLayoutPanel();
            root.Dock = DockStyle.Fill;
            root.Padding = new Padding(12);
            root.ColumnCount = 1;
            root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
            root.RowCount = 3;
            root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            root.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));

            Label lbl = new Label();
            lbl.Text = "Учётная запись: " + user + Environment.NewLine + (hint ?? "");
            lbl.AutoSize = true;
            lbl.MaximumSize = new Size(400, 0);

            _txt = new TextBox();
            _txt.UseSystemPasswordChar = true;
            _txt.Dock = DockStyle.Fill;

            FlowLayoutPanel buttons = new FlowLayoutPanel();
            buttons.Dock = DockStyle.Fill;
            buttons.FlowDirection = FlowDirection.RightToLeft;
            buttons.WrapContents = false;
            buttons.AutoSize = true;
            buttons.AutoSizeMode = AutoSizeMode.GrowAndShrink;

            Button ok = new Button();
            ok.Text = "OK";
            ok.DialogResult = DialogResult.OK;
            ok.AutoSize = true;
            ok.AutoSizeMode = AutoSizeMode.GrowAndShrink;
            ok.MinimumSize = new Size(80, 30);
            ok.Padding = new Padding(8, 0, 8, 0);
            ok.Margin = new Padding(6, 3, 3, 3);

            Button cancel = new Button();
            cancel.Text = "Отмена";
            cancel.DialogResult = DialogResult.Cancel;
            cancel.AutoSize = true;
            cancel.AutoSizeMode = AutoSizeMode.GrowAndShrink;
            cancel.MinimumSize = new Size(90, 30);
            cancel.Padding = new Padding(8, 0, 8, 0);
            cancel.Margin = new Padding(3, 3, 6, 3);

            buttons.Controls.Add(ok);
            buttons.Controls.Add(cancel);

            root.Controls.Add(lbl, 0, 0);
            root.Controls.Add(_txt, 0, 1);
            root.Controls.Add(buttons, 0, 2);
            Controls.Add(root);

            AcceptButton = ok;
            CancelButton = cancel;
        }
    }
}
