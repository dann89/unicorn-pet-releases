using System;
using System.Collections.Generic;
using System.Windows.Controls;
using UnicornPet.Core;

namespace UnicornPet.App
{
    public static class PetMenu
    {
        public static ContextMenu ToWpf(IReadOnlyList<MenuItemModel> items)
        {
            var m = new ContextMenu();
            Fill(m.Items, items);
            return m;
        }

        static void Fill(ItemCollection target, IReadOnlyList<MenuItemModel> items)
        {
            foreach (var i in items)
            {
                if (i.IsSeparator) { target.Add(new Separator()); continue; }
                var mi = new MenuItem { Header = i.Text, IsChecked = i.IsChecked };
                if (i.Children != null) Fill(mi.Items, i.Children);
                var click = i.OnClick;
                if (click != null) mi.Click += (s, e) => click();
                target.Add(mi);
            }
        }

        public static void ToStrip(IReadOnlyList<MenuItemModel> items, System.Windows.Forms.ContextMenuStrip strip)
        {
            strip.Items.Clear();
            Fill(strip.Items, items);
        }

        static void Fill(System.Windows.Forms.ToolStripItemCollection target, IReadOnlyList<MenuItemModel> items)
        {
            foreach (var i in items)
            {
                if (i.IsSeparator) { target.Add(new System.Windows.Forms.ToolStripSeparator()); continue; }
                var mi = new System.Windows.Forms.ToolStripMenuItem(i.Text) { Checked = i.IsChecked };
                if (i.Children != null) Fill(mi.DropDownItems, i.Children);
                var click = i.OnClick;
                if (click != null) mi.Click += (s, e) => click();
                target.Add(mi);
            }
        }
    }
}
