using System.IO;
using System.Windows;
using System.Windows.Controls;
using Microsoft.Win32;

namespace eFakturaViewer;

internal static class WorkflowDialog
{
    internal static bool Edit(Window owner) => Build(owner).ShowDialog() == true;
    internal static AppDialog Build(Window owner)
    {
        var folders = WorkflowStore.Load().Folders.ToList();
        var dialog = new AppDialog(owner, "Cieľové priečinky", "Prvý priečinok je predvolený pre nepriradené faktúry. Poradie určuje čísla tlačidiel.");
        dialog.Width = Math.Max(380, Math.Min(720, owner.ActualWidth));
        var list = new StackPanel();
        var scroll = new ScrollViewer { Content = list, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, MaxHeight = Math.Max(150, Math.Min(420, owner.ActualHeight - 280)), Margin = new Thickness(0, 14, 0, 0) };
        dialog.AddContent(scroll);
        var editors = new List<(WorkflowFolder Folder, TextBox Name, TextBox Path)>();
        void ReadEditors()
        {
            folders = editors.Select(x => x.Folder with { Name = x.Name.Text, Path = x.Path.Text }).ToList();
        }
        void Draw()
        {
            list.Children.Clear(); editors.Clear();
            for (int i = 0; i < folders.Count; i++)
            {
                int index = i; var folder = folders[i];
                var frame = new Border { CornerRadius = new CornerRadius(9), BorderThickness = new Thickness(1), Padding = new Thickness(10), Margin = new Thickness(0, 0, 0, 8) };
                frame.SetResourceReference(Border.BackgroundProperty, "Input"); frame.SetResourceReference(Border.BorderBrushProperty, "Border");
                var panel = new StackPanel(); frame.Child = panel; list.Children.Add(frame);
                var top = new DockPanel(); panel.Children.Add(top);
                var number = new TextBlock { Text = (i + 1) + (i == 0 ? " · predvolený" : ""), Width = 105, FontSize = 12, VerticalAlignment = VerticalAlignment.Center };
                top.Children.Add(number);
                var actions = new StackPanel { Orientation = Orientation.Horizontal }; DockPanel.SetDock(actions, Dock.Right); top.Children.Add(actions);
                var up = VisualStyle.Button("", "↑", "Posunúť vyššie", () => { ReadEditors(); (folders[index - 1], folders[index]) = (folders[index], folders[index - 1]); Draw(); }); up.IsEnabled = index > 0;
                var down = VisualStyle.Button("", "↓", "Posunúť nižšie", () => { ReadEditors(); (folders[index + 1], folders[index]) = (folders[index], folders[index + 1]); Draw(); }); down.IsEnabled = index < folders.Count - 1;
                var remove = VisualStyle.Button("close", "", "Odstrániť cieľ z nastavení", () => { ReadEditors(); folders.RemoveAt(index); Draw(); });
                foreach (var b in new[] { up, down, remove }) { b.Padding = new Thickness(5); b.MinHeight = 28; actions.Children.Add(b); }
                var name = new TextBox { Text = folder.Name, Margin = new Thickness(0, 0, 6, 0), MaxLength = 120, ToolTip = "Názov priečinka v aplikácii" }; top.Children.Add(name);
                var bottom = new DockPanel { Margin = new Thickness(0, 8, 0, 0) }; panel.Children.Add(bottom);
                var path = new TextBox { Text = folder.Path, ToolTip = "Úplná cesta k cieľovému priečinku", MinWidth = 100 };
                var browse = VisualStyle.Button("folder", "", "Vybrať priečinok", () =>
                {
                    var picker = new OpenFolderDialog { Title = "Cieľový priečinok", InitialDirectory = Directory.Exists(path.Text) ? path.Text : "" };
                    if (picker.ShowDialog(dialog) == true) path.Text = picker.FolderName;
                });
                DockPanel.SetDock(browse, Dock.Right); bottom.Children.Add(browse); bottom.Children.Add(path);
                editors.Add((folder, name, path));
            }
            if (folders.Count == 0) list.Children.Add(new TextBlock { Text = "Zatiaľ nemáte nastavený žiadny cieľový priečinok.", TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 8, 0, 8) });
        }
        Draw();
        dialog.AddContent(VisualStyle.Button("plus", "Pridať priečinok", "Pridať cieľový priečinok", () =>
        {
            var picker = new OpenFolderDialog { Title = "Vyberte cieľový priečinok" };
            if (picker.ShowDialog(dialog) != true) return;
            ReadEditors(); var name = Path.GetFileName(Path.TrimEndingDirectorySeparator(picker.FolderName));
            folders.Add(new(Guid.NewGuid(), name.Length > 0 ? name : picker.FolderName, picker.FolderName)); Draw(); scroll.ScrollToBottom();
        }));
        var cancel = VisualStyle.Button("", "Zrušiť", "Zahodiť zmeny", () => dialog.DialogResult = false); cancel.IsCancel = true;
        var save = VisualStyle.Button("", "Uložiť", "Uložiť cieľové priečinky", () =>
        {
            try { ReadEditors(); WorkflowStore.SetFolders(folders); dialog.DialogResult = true; }
            catch (Exception e) { dialog.Message(e.Message); }
        });
        save.IsDefault = true; save.SetResourceReference(Button.BackgroundProperty, "Accent"); save.SetResourceReference(Button.ForegroundProperty, "AccentText");
        dialog.Actions.Children.Add(cancel); dialog.Actions.Children.Add(save); return dialog;
    }
}
