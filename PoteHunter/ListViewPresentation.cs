namespace PoteHunter;

internal readonly record struct ListViewPresentationWork(int Added, int Removed, int Updated, int Moved);

// Presentation only: callers still perform the original observations and guards.
// Keys describe row identity, not mutable display text such as HP or range.
internal static class ListViewPresentation
{
    internal static ListViewPresentationWork Reconcile(ListView view, IReadOnlyList<ListViewItem> desired, Func<ListViewItem,string> key)
    {
        var existing = view.Items.Cast<ListViewItem>().ToArray();
        var byKey = new Dictionary<string,ListViewItem>(StringComparer.Ordinal);
        var wanted = new HashSet<string>(StringComparer.Ordinal);
        bool unique = existing.All(item => byKey.TryAdd(key(item), item)) && desired.All(item => wanted.Add(key(item)));
        string? top = view.IsHandleCreated && view.TopItem is ListViewItem first ? key(first) : null;
        int added = 0, removed = 0, updated = 0, moved = 0;
        view.BeginUpdate();
        try
        {
            // Ambiguous keys retain the previous complete replacement behavior.
            if(!unique)
            {
                removed = view.Items.Count; view.Items.Clear();
                foreach(var item in desired)view.Items.Add(item);
                return new(desired.Count, removed, 0, 0);
            }
            foreach(var item in existing)if(!wanted.Contains(key(item))) { view.Items.Remove(item); removed++; }
            for(int i = 0; i < desired.Count; i++)
            {
                var presentation = desired[i]; string identity = key(presentation);
                if(!byKey.TryGetValue(identity, out var row))
                { view.Items.Insert(i, presentation); byKey[identity] = presentation; added++; continue; }
                if(Synchronize(view, row, presentation))updated++;
                if(row.Index != i)
                {
                    bool selected = row.Selected; view.Items.Remove(row); view.Items.Insert(i, row); row.Selected = selected; moved++;
                }
                if(presentation.Selected)row.Selected = true;
            }
        }
        finally
        {
            if(top != null && byKey.TryGetValue(top, out var row) && row.ListView == view && view.IsHandleCreated)view.TopItem = row;
            view.EndUpdate();
        }
        return new(added, removed, updated, moved);
    }

    static bool Synchronize(ListView view, ListViewItem row, ListViewItem desired)
    {
        bool changed = false;
        while(row.SubItems.Count > desired.SubItems.Count) { row.SubItems.RemoveAt(row.SubItems.Count - 1); changed = true; }
        for(int i = 0; i < desired.SubItems.Count; i++)
        {
            if(i >= row.SubItems.Count) { row.SubItems.Add(desired.SubItems[i].Text); changed = true; }
            else if(row.SubItems[i].Text != desired.SubItems[i].Text) { row.SubItems[i].Text = desired.SubItems[i].Text; changed = true; }
        }
        if(row.ToolTipText != desired.ToolTipText) { row.ToolTipText = desired.ToolTipText; changed = true; }
        if(!Equals(row.Tag, desired.Tag)) { row.Tag = desired.Tag; changed = true; }
        // Unattached default rows inherit the destination ListView's theme.
        Color foreground = desired.ForeColor == SystemColors.WindowText ? view.ForeColor : desired.ForeColor;
        Color background = desired.BackColor == SystemColors.Window ? view.BackColor : desired.BackColor;
        if(row.ForeColor != foreground) { row.ForeColor = foreground; changed = true; }
        if(row.BackColor != background) { row.BackColor = background; changed = true; }
        return changed;
    }
}
