using System;
using System.Drawing;
using System.Windows.Forms;

namespace WinCal
{
    // Диалог настроек: список подписок + общие параметры + секция Thunderbird.
    public class SettingsForm : Form
    {
        readonly AppConfig cfg;
        readonly ListView lv;
        readonly NumericUpDown numInterval, numForward, numBackward;
        readonly TextBox tbPrefix;
        readonly CheckBox chkTb;
        readonly TextBox tbTbProfile, tbTbCals;

        public SettingsForm(AppConfig cfg)
        {
            this.cfg = cfg;
            Text = "WinCalendar — настройки";
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            MinimizeBox = false;
            StartPosition = FormStartPosition.CenterScreen;
            Font = new Font("Segoe UI", 9f);
            ClientSize = new Size(680, 590);

            var lblSources = new Label { Text = "Подписки (iCal):", Location = new Point(12, 12), AutoSize = true };
            Controls.Add(lblSources);

            lv = new ListView
            {
                Location = new Point(12, 34),
                Size = new Size(656, 200),
                View = View.Details,
                FullRowSelect = true,
                HideSelection = false
            };
            lv.Columns.Add("Вкл", 42);
            lv.Columns.Add("Имя", 130);
            lv.Columns.Add("Тип", 52);
            lv.Columns.Add("Адрес (URL или файл)", 340);
            lv.Columns.Add("Цвет", 80);
            lv.DoubleClick += delegate { EditSource(); };
            Controls.Add(lv);

            var btnAdd = new Button { Text = "Добавить…", Location = new Point(12, 242), Size = new Size(105, 27) };
            btnAdd.Click += delegate { AddSource(); };
            var btnEdit = new Button { Text = "Изменить…", Location = new Point(125, 242), Size = new Size(105, 27) };
            btnEdit.Click += delegate { EditSource(); };
            var btnDel = new Button { Text = "Удалить", Location = new Point(238, 242), Size = new Size(105, 27) };
            btnDel.Click += delegate { DelSource(); };
            Controls.Add(btnAdd);
            Controls.Add(btnEdit);
            Controls.Add(btnDel);

            var gbSync = new GroupBox
            {
                Text = "Синхронизация",
                Location = new Point(12, 284),
                Size = new Size(656, 96)
            };
            Controls.Add(gbSync);

            var lblInterval = new Label { Text = "Каждые (минут):", Location = new Point(12, 28), AutoSize = true };
            numInterval = new NumericUpDown { Location = new Point(120, 25), Size = new Size(70, 24), Minimum = 5, Maximum = 1440 };
            var lblForward = new Label { Text = "Вперёд (дней):", Location = new Point(220, 28), AutoSize = true };
            numForward = new NumericUpDown { Location = new Point(330, 25), Size = new Size(70, 24), Minimum = 1, Maximum = 365 };
            var lblBackward = new Label { Text = "Назад (дней):", Location = new Point(430, 28), AutoSize = true };
            numBackward = new NumericUpDown { Location = new Point(540, 25), Size = new Size(70, 24), Minimum = 0, Maximum = 30 };
            var lblPrefix = new Label { Text = "Префикс имён календарей:", Location = new Point(12, 60), AutoSize = true };
            tbPrefix = new TextBox { Location = new Point(180, 57), Width = 120 };
            gbSync.Controls.Add(lblInterval);
            gbSync.Controls.Add(numInterval);
            gbSync.Controls.Add(lblForward);
            gbSync.Controls.Add(numForward);
            gbSync.Controls.Add(lblBackward);
            gbSync.Controls.Add(numBackward);
            gbSync.Controls.Add(lblPrefix);
            gbSync.Controls.Add(tbPrefix);

            var gbTb = new GroupBox
            {
                Text = "Thunderbird",
                Location = new Point(12, 390),
                Size = new Size(656, 110)
            };
            Controls.Add(gbTb);

            chkTb = new CheckBox { Text = "Читать календари Thunderbird (storage и кэшированные; ICS — напрямую)", Location = new Point(12, 24), AutoSize = true };
            var lblTbProfile = new Label { Text = "Профиль (пусто = авто):", Location = new Point(12, 52), AutoSize = true };
            tbTbProfile = new TextBox { Location = new Point(180, 49), Width = 400 };
            var lblTbCals = new Label { Text = "Календари (через запятую, пусто = все):", Location = new Point(12, 80), AutoSize = true };
            tbTbCals = new TextBox { Location = new Point(280, 77), Width = 300 };
            gbTb.Controls.Add(chkTb);
            gbTb.Controls.Add(lblTbProfile);
            gbTb.Controls.Add(tbTbProfile);
            gbTb.Controls.Add(lblTbCals);
            gbTb.Controls.Add(tbTbCals);

            var btnOk = new Button { Text = "OK", Location = new Point(468, 548), Size = new Size(96, 30) };
            btnOk.Click += delegate { SaveAndClose(); };
            var btnCancel = new Button { Text = "Отмена", Location = new Point(572, 548), Size = new Size(96, 30), DialogResult = DialogResult.Cancel };
            Controls.Add(btnOk);
            Controls.Add(btnCancel);
            AcceptButton = btnOk;
            CancelButton = btnCancel;

            LoadFromConfig();
        }

        void LoadFromConfig()
        {
            numInterval.Value = Clamp(cfg.syncIntervalMinutes, 5, 1440);
            numForward.Value = Clamp(cfg.daysForward, 1, 365);
            numBackward.Value = Clamp(cfg.daysBackward, 0, 30);
            tbPrefix.Text = cfg.prefix ?? "";
            chkTb.Checked = cfg.thunderbird.enabled;
            tbTbProfile.Text = cfg.thunderbird.profile ?? "";
            tbTbCals.Text = cfg.thunderbird.calendars == null ? "" : string.Join(", ", cfg.thunderbird.calendars.ToArray());
            FillList();
        }

        static decimal Clamp(int v, int min, int max)
        {
            if (v < min) return min;
            if (v > max) return max;
            return v;
        }

        void FillList()
        {
            lv.Items.Clear();
            foreach (var s in cfg.sources)
            {
                var isUrl = !string.IsNullOrEmpty(s.url);
                var item = new ListViewItem(s.enabled ? "да" : "нет");
                item.SubItems.Add(s.name);
                item.SubItems.Add(isUrl ? "URL" : "файл");
                item.SubItems.Add(isUrl ? s.url : s.path);
                item.SubItems.Add(s.color);
                item.Tag = s;
                lv.Items.Add(item);
            }
        }

        void AddSource()
        {
            var s = new IcsSource { color = "#0078D7" };
            if (SourceEditForm.Edit(s))
            {
                cfg.sources.Add(s);
                FillList();
            }
        }

        void EditSource()
        {
            if (lv.SelectedItems.Count == 0) return;
            var s = (IcsSource)lv.SelectedItems[0].Tag;
            if (SourceEditForm.Edit(s)) FillList();
        }

        void DelSource()
        {
            if (lv.SelectedItems.Count == 0) return;
            var s = (IcsSource)lv.SelectedItems[0].Tag;
            cfg.sources.Remove(s);
            FillList();
        }

        void SaveAndClose()
        {
            if (cfg.sources.Count > 0)
            {
                foreach (var s in cfg.sources)
                {
                    if (string.IsNullOrEmpty(s.name))
                    {
                        MessageBox.Show("У подписки не заполнено имя.", "WinCalendar",
                            MessageBoxButtons.OK, MessageBoxIcon.Warning);
                        return;
                    }
                    if (string.IsNullOrEmpty(s.url) && string.IsNullOrEmpty(s.path))
                    {
                        MessageBox.Show("У подписки \"" + s.name + "\" не указан адрес.", "WinCalendar",
                            MessageBoxButtons.OK, MessageBoxIcon.Warning);
                        return;
                    }
                }
            }
            cfg.syncIntervalMinutes = (int)numInterval.Value;
            cfg.daysForward = (int)numForward.Value;
            cfg.daysBackward = (int)numBackward.Value;
            cfg.prefix = tbPrefix.Text.Trim();
            cfg.thunderbird.enabled = chkTb.Checked;
            cfg.thunderbird.profile = tbTbProfile.Text.Trim();
            var cals = tbTbCals.Text.Trim();
            if (cals.Length == 0) cfg.thunderbird.calendars = null;
            else
            {
                cfg.thunderbird.calendars = new System.Collections.Generic.List<string>();
                foreach (var part in cals.Split(',')) cfg.thunderbird.calendars.Add(part.Trim());
            }
            DialogResult = DialogResult.OK;
        }
    }

    // Редактор одной подписки.
    public class SourceEditForm : Form
    {
        readonly IcsSource src;
        readonly TextBox tbName, tbAddr, tbUser, tbPass;
        readonly NumericUpDown numReminder;
        readonly CheckBox chkEnabled;
        readonly Button btnColor;
        Color color = Color.FromArgb(0, 120, 215);

        public SourceEditForm(IcsSource src)
        {
            this.src = src;
            Text = "Подписка";
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            MinimizeBox = false;
            StartPosition = FormStartPosition.CenterScreen;
            Font = new Font("Segoe UI", 9f);
            ClientSize = new Size(520, 330);

            var lblName = new Label { Text = "Имя календаря:", Location = new Point(12, 16), AutoSize = true };
            tbName = new TextBox { Location = new Point(160, 13), Width = 340 };
            var lblAddr = new Label { Text = "URL или путь к файлу\n(webcal:// тоже можно):", Location = new Point(12, 45), AutoSize = true };
            tbAddr = new TextBox { Location = new Point(160, 45), Width = 340 };
            var lblUser = new Label { Text = "Логин (Basic):", Location = new Point(12, 105), AutoSize = true };
            tbUser = new TextBox { Location = new Point(160, 102), Width = 340 };
            var lblPass = new Label { Text = "Пароль:", Location = new Point(12, 135), AutoSize = true };
            tbPass = new TextBox { Location = new Point(160, 132), Width = 340, UseSystemPasswordChar = true };
            var lblReminder = new Label { Text = "Напоминание, мин (-1 = нет):", Location = new Point(12, 170), AutoSize = true };
            numReminder = new NumericUpDown { Location = new Point(215, 167), Width = 80, Minimum = -1, Maximum = 10080 };
            chkEnabled = new CheckBox { Text = "Включена", Location = new Point(12, 200), AutoSize = true };
            var lblColor = new Label { Text = "Цвет календаря:", Location = new Point(12, 232), AutoSize = true };
            btnColor = new Button { Location = new Point(160, 227), Size = new Size(80, 28) };
            btnColor.Click += delegate { PickColor(); };

            var btnOk = new Button { Text = "OK", Location = new Point(308, 282), Size = new Size(92, 30) };
            btnOk.Click += delegate { Apply(); };
            var btnCancel = new Button { Text = "Отмена", Location = new Point(408, 282), Size = new Size(92, 30), DialogResult = DialogResult.Cancel };
            Controls.AddRange(new Control[]
            {
                lblName, tbName, lblAddr, tbAddr, lblUser, tbUser, lblPass, tbPass,
                lblReminder, numReminder, chkEnabled, lblColor, btnColor, btnOk, btnCancel
            });
            AcceptButton = btnOk;
            CancelButton = btnCancel;

            tbName.Text = src.name;
            tbAddr.Text = string.IsNullOrEmpty(src.url) ? src.path : src.url;
            tbUser.Text = src.user;
            tbPass.Text = src.password;
            numReminder.Value = Clamp(src.reminderMinutes);
            chkEnabled.Checked = src.enabled;
            TryParseColor(src.color);
            UpdateColorButton();
        }

        static decimal Clamp(int v)
        {
            if (v < -1) return -1;
            if (v > 10080) return 10080;
            return v;
        }

        void PickColor()
        {
            using (var dlg = new ColorDialog { FullOpen = true, Color = color })
            {
                if (dlg.ShowDialog(this) == DialogResult.OK)
                {
                    color = dlg.Color;
                    UpdateColorButton();
                }
            }
        }

        void UpdateColorButton()
        {
            btnColor.BackColor = color;
            btnColor.Text = string.Format("#{0:X2}{1:X2}{2:X2}", color.R, color.G, color.B);
            btnColor.ForeColor = (color.R + color.G + color.B) > 384 ? Color.Black : Color.White;
        }

        void TryParseColor(string hex)
        {
            try
            {
                var s = (hex ?? "").TrimStart('#');
                if (s.Length == 6)
                    color = Color.FromArgb(
                        Convert.ToInt32(s.Substring(0, 2), 16),
                        Convert.ToInt32(s.Substring(2, 2), 16),
                        Convert.ToInt32(s.Substring(4, 2), 16));
            }
            catch { }
        }

        void Apply()
        {
            if (tbName.Text.Trim().Length == 0)
            {
                MessageBox.Show("Укажите имя календаря.", "WinCalendar",
                    MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }
            if (tbAddr.Text.Trim().Length == 0)
            {
                MessageBox.Show("Укажите URL или путь к файлу.", "WinCalendar",
                    MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }
            src.name = tbName.Text.Trim();
            var addr = tbAddr.Text.Trim();
            var low = addr.ToLowerInvariant();
            if (low.StartsWith("http://") || low.StartsWith("https://") || low.StartsWith("webcal://"))
            {
                src.url = addr;
                src.path = "";
            }
            else
            {
                src.path = addr;
                src.url = "";
            }
            src.user = tbUser.Text.Trim();
            src.password = tbPass.Text;
            src.reminderMinutes = (int)numReminder.Value;
            src.enabled = chkEnabled.Checked;
            src.color = string.Format("#{0:X2}{1:X2}{2:X2}", color.R, color.G, color.B);
            DialogResult = DialogResult.OK;
        }

        public static bool Edit(IcsSource s)
        {
            using (var f = new SourceEditForm(s))
            {
                return f.ShowDialog() == DialogResult.OK;
            }
        }
    }
}
