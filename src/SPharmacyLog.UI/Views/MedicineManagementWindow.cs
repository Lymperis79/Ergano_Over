using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Platform.Storage;
using Microsoft.EntityFrameworkCore;
using SPharmacyLog.Data;
using SPharmacyLog.Data.Entities;
using SPharmacyLog.UI.Localization;
using System.Text;

namespace SPharmacyLog.UI.Views;

public sealed class MedicineManagementWindow : Window
{
    private readonly TextBox search = new() { MinWidth = 260, PlaceholderText = "Search medicine..." };
    private readonly StackPanel rows = new() { Spacing = 4 };
    private readonly TextBlock status = new() { TextWrapping = TextWrapping.Wrap, Foreground = Brushes.LightGray };
    private readonly TextBlock pageInfo = new() { VerticalAlignment = VerticalAlignment.Center };
    private readonly Button previousPage = new() { Content = Tr.Loc.TranslateText("Previous"), MinWidth = 90 };
    private readonly Button nextPage = new() { Content = Tr.Loc.TranslateText("Next"), MinWidth = 90 };
    private const int PageSize = 50;
    private int currentPage = 1;
    private int totalCount;
    private CancellationTokenSource? searchDebounce;
    private List<MedicineCatalogItem> loaded = new();

    public MedicineManagementWindow()
    {
        Title = Tr.Loc.TranslateText("Medicine");
        Width = 980; Height = 720; MinWidth = 720; MinHeight = 500;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;

        var title = new TextBlock { Text = Tr.Loc.TranslateText("Medicine"), FontSize = 25, FontWeight = FontWeight.Bold };
        var newButton = new Button { Content = Tr.Loc.TranslateText("＋ Add medicine"), MinHeight = 36, Classes = { "accent" } };
        newButton.Click += async (_, _) => await EditMedicineAsync(null);
        var refresh = new Button { Content = Tr.Loc.TranslateText("Refresh"), MinHeight = 36 };
        refresh.Click += async (_, _) => await LoadAsync();
        var export = new Button { Content = Tr.Loc.TranslateText("Export to Excel"), MinHeight = 36 };
        export.Click += async (_, _) => await ExportAsync();
        var import = new Button { Content = Tr.Loc.TranslateText("Import from Excel"), MinHeight = 36 };
        import.Click += async (_, _) => await ImportAsync();
        var searchButton = new Button { Content = Tr.Loc.TranslateText("Search") };
        searchButton.Click += async (_, _) => await LoadAsync();
        search.KeyDown += async (_, e) => { if (e.Key == Avalonia.Input.Key.Enter) { e.Handled = true; searchDebounce?.Cancel(); await LoadAsync(resetPage: true); } };
        search.TextChanged += (_, _) => ScheduleSearch();
        previousPage.Click += async (_, _) => { if (currentPage > 1) { currentPage--; await LoadAsync(); } };
        nextPage.Click += async (_, _) => { if (currentPage < TotalPages) { currentPage++; await LoadAsync(); } };

        var header = new Grid { ColumnDefinitions = new ColumnDefinitions("2*,2*,*,Auto"), Background = new SolidColorBrush(Color.Parse("#263447")) };
        string[] labels = { "Name", "Barcode", "Notes", "Edit" };
        for (int col=0;col<labels.Length;col++)
        {
            var tb = new TextBlock { Text = Tr.Loc.TranslateText(labels[col]), FontWeight = FontWeight.Bold, Margin = new Thickness(9,8), VerticalAlignment = VerticalAlignment.Center };
            Grid.SetColumn(tb,col); header.Children.Add(tb);
        }
        var toolbar = new StackPanel { Orientation=Orientation.Horizontal, Spacing=8, Children={search,searchButton,newButton,import,export,refresh} };
        var pager = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Right, Children = { previousPage, pageInfo, nextPage } };
        var listArea = new Grid { RowDefinitions = new RowDefinitions("Auto,*,Auto"), RowSpacing = 8, Children = { header, new ScrollViewer { Content=rows, VerticalScrollBarVisibility=Avalonia.Controls.Primitives.ScrollBarVisibility.Auto, HorizontalScrollBarVisibility=Avalonia.Controls.Primitives.ScrollBarVisibility.Auto }, pager } };
        Grid.SetRow(listArea.Children[1], 1); Grid.SetRow(listArea.Children[2], 2);
        var content = new Grid { Margin = new Thickness(18), RowDefinitions = new RowDefinitions("Auto,Auto,Auto,*"), RowSpacing=10, Children={title,toolbar,status,listArea} };
        for (int n=0;n<content.Children.Count;n++) Grid.SetRow(content.Children[n],n);
        Content=content;
        _=LoadAsync();
    }

    private int TotalPages => Math.Max(1, (int)Math.Ceiling(totalCount / (double)PageSize));

    private void ScheduleSearch()
    {
        searchDebounce?.Cancel();
        searchDebounce?.Dispose();
        var cts = new CancellationTokenSource();
        searchDebounce = cts;
        _ = Task.Run(async () =>
        {
            try
            {
                await Task.Delay(300, cts.Token);
                await Avalonia.Threading.Dispatcher.UIThread.InvokeAsync(async () =>
                {
                    if (!cts.IsCancellationRequested) await LoadAsync(resetPage: true);
                });
            }
            catch (OperationCanceledException) { }
        });
    }

    private async Task LoadAsync(bool resetPage = false)
    {
        try
        {
            if (resetPage) currentPage = 1;
            await using var db = new PharmacyDbContext(PharmacyDb.Options());
            IQueryable<MedicineCatalogItem> query = db.Medicines.AsNoTracking();
            var term = search.Text?.Trim();
            if (!string.IsNullOrWhiteSpace(term))
            {
                query = query.Where(m =>
                    m.Name.Contains(term)
                    || (m.Barcode != null && m.Barcode.Contains(term))
                    || m.Barcodes.Any(b => b.BarcodeValue.Contains(term)));
            }

            totalCount = await query.CountAsync();
            currentPage = Math.Clamp(currentPage, 1, TotalPages);
            loaded = await query.OrderBy(m => m.Name).ThenBy(m => m.Id)
                .Skip((currentPage - 1) * PageSize).Take(PageSize).ToListAsync();
            rows.Children.Clear();
            foreach(var medicine in loaded)
            {
                var grid=new Grid { ColumnDefinitions=new ColumnDefinitions("2*,2*,*,Auto"), MinWidth=850 };
                var values=new[]{medicine.Name,medicine.Barcode??"",medicine.Notes??""};
                for(int col=0;col<values.Length;col++)
                {
                    var tb=new TextBlock { Text=values[col], TextWrapping=TextWrapping.Wrap, Margin=new Thickness(9,7), VerticalAlignment=VerticalAlignment.Center };
                    Grid.SetColumn(tb,col);grid.Children.Add(tb);
                }
                var editButton=new Button { Content=Tr.Loc.TranslateText("Edit"), MinWidth=72 };
                editButton.Click += async (_,_)=>await EditMedicineAsync(medicine);
                var buttons=new StackPanel { Orientation=Orientation.Horizontal, Spacing=5, Children={editButton} };
                Grid.SetColumn(buttons,3);grid.Children.Add(buttons);
                rows.Children.Add(new Border { BorderBrush=new SolidColorBrush(Color.Parse("#354152")), BorderThickness=new Thickness(0,0,0,1), Child=grid });
            }
            status.Text = $"{Tr.Loc.TranslateText("Medicines")}: {totalCount} | {Tr.Loc.TranslateText("Shown")}: {loaded.Count}";
            pageInfo.Text = $"{Tr.Loc.TranslateText("Page")} {currentPage} / {TotalPages}";
            previousPage.IsEnabled = currentPage > 1;
            nextPage.IsEnabled = currentPage < TotalPages;
        }
        catch(Exception ex){status.Text=Tr.Loc.TranslateText("Could not load medicines")+": "+ex.Message;}
    }

    private async Task EditMedicineAsync(MedicineCatalogItem? existing)
    {
        var name=new TextBox { Text=existing?.Name??"", MinWidth=320 };
        var barcode=new TextBox { Text=existing?.Barcode??"", MinWidth=320 };
        var notes=new TextBox { Text=existing?.Notes??"", MinWidth=320, AcceptsReturn=true, Height=100, TextWrapping=TextWrapping.Wrap };
        var active=new CheckBox { Content=Tr.Loc.TranslateText("Active"), IsChecked=existing?.IsActive??true };
        var form=new StackPanel { Spacing=10, Margin=new Thickness(18) };
        foreach(var (label,control) in new (string,Control)[]{("Name",name),("Barcode",barcode),("Notes",notes)})
        {form.Children.Add(new TextBlock { Text=Tr.Loc.TranslateText(label), FontWeight=FontWeight.SemiBold });form.Children.Add(control);}
        form.Children.Add(active);
        var dialog=new Window { Title=Tr.Loc.TranslateText(existing==null?"Add medicine":"Edit medicine"), Width=500,Height=540,MinWidth=440,WindowStartupLocation=WindowStartupLocation.CenterOwner };
        var save=new Button { Content=Tr.Loc.TranslateText("Save"),MinWidth=100,Classes={"accent"} };
        var cancel=new Button { Content=Tr.Loc.TranslateText("Cancel"),MinWidth=100 };
        var footer=new StackPanel { Orientation=Orientation.Horizontal,Spacing=8,HorizontalAlignment=HorizontalAlignment.Right,Children={save,cancel} };
        var root=new Grid { RowDefinitions=new RowDefinitions("*,Auto"),Children={new ScrollViewer{Content=form},footer},Margin=new Thickness(12) };
        Grid.SetRow(footer,1);dialog.Content=root;
        cancel.Click+=(_,_)=>dialog.Close();
        save.Click+=async (_,_)=>{
            if(string.IsNullOrWhiteSpace(name.Text)){status.Text=Tr.Loc.TranslateText("Medicine name is required.");return;}
            await using var db=new PharmacyDbContext(PharmacyDb.Options());
            MedicineCatalogItem item;
            if(existing is null){item=new MedicineCatalogItem();db.Medicines.Add(item);}
            else {item=await db.Medicines.FirstOrDefaultAsync(x=>x.Id==existing.Id)??new MedicineCatalogItem();if(item.Id==0)db.Medicines.Add(item);}
            item.Name=name.Text.Trim();item.Barcode=string.IsNullOrWhiteSpace(barcode.Text)?null:barcode.Text.Trim();item.Notes=string.IsNullOrWhiteSpace(notes.Text)?null:notes.Text.Trim();item.IsActive=active.IsChecked==true;
            await db.SaveChangesAsync();dialog.Close();await LoadAsync(resetPage: existing is null);
        };
        await dialog.ShowDialog(this);
    }

    private async Task ExportAsync()
    {
        var file=await StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions{Title=Tr.Loc.TranslateText("Export medicines"),SuggestedFileName="medicines.csv",DefaultExtension="csv",FileTypeChoices=new[]{new FilePickerFileType("Excel-compatible CSV"){Patterns=new[]{"*.csv"}}}});
        if(file is null)return;
        await using var stream=await file.OpenWriteAsync();await using var writer=new StreamWriter(stream,new UTF8Encoding(true));
        await writer.WriteLineAsync("Name,Barcode,Notes,IsActive");
        await using (var db = new PharmacyDbContext(PharmacyDb.Options()))
        {
            await foreach (var m in db.Medicines.AsNoTracking().OrderBy(m => m.Name).AsAsyncEnumerable())
                await writer.WriteLineAsync(string.Join(",",new[]{m.Name,m.Barcode??"",m.Notes??"",m.IsActive.ToString()}.Select(CsvEscape)));
        }
        status.Text=Tr.Loc.TranslateText("Medicine list exported.");
    }

    private async Task ImportAsync()
    {
        var file=await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions{Title=Tr.Loc.TranslateText("Import medicines"),AllowMultiple=false,FileTypeFilter=new[]{new FilePickerFileType("Excel-compatible CSV"){Patterns=new[]{"*.csv"}}}});
        if(file.Count==0)return;
        try
        {
            await using var stream=await file[0].OpenReadAsync();using var reader=new StreamReader(stream,Encoding.UTF8,true);
            var rows=new List<string[]>();string? line;while((line=await reader.ReadLineAsync())!=null)rows.Add(ParseCsv(line));
            if(rows.Count<2){status.Text=Tr.Loc.TranslateText("Import file has no medicine rows.");return;}
            var headers=rows[0].Select(h=>h.Trim()).ToArray();int ix(string name)=>Array.FindIndex(headers,h=>h.Equals(name,StringComparison.OrdinalIgnoreCase));
            int nameIx=ix("Name"),barcodeIx=ix("Barcode"),notesIx=ix("Notes"),activeIx=ix("IsActive");
            if(nameIx<0){status.Text=Tr.Loc.TranslateText("CSV requires a Name column.");return;}
            await using var db=new PharmacyDbContext(PharmacyDb.Options());var existing=await db.Medicines.ToListAsync();int added=0,updated=0;
            foreach(var row in rows.Skip(1))
            {
                string val(int idx)=>idx>=0&&idx<row.Length?row[idx].Trim():"";
                var nm=val(nameIx);if(string.IsNullOrWhiteSpace(nm))continue;
                var bc=val(barcodeIx);
                var match=existing.FirstOrDefault(m=>!string.IsNullOrWhiteSpace(bc)&&string.Equals(m.Barcode,bc,StringComparison.OrdinalIgnoreCase))
                    ??existing.FirstOrDefault(m=>string.Equals(m.Name,nm,StringComparison.CurrentCultureIgnoreCase));
                if(match is null){match=new MedicineCatalogItem();db.Medicines.Add(match);existing.Add(match);added++;}
                else updated++;
                match.Name=nm;match.Barcode=string.IsNullOrWhiteSpace(bc)?null:bc;match.Notes=val(notesIx);match.IsActive=activeIx<0||!bool.TryParse(val(activeIx),out var isActive)||isActive;
            }
            await db.SaveChangesAsync();status.Text=$"{Tr.Loc.TranslateText("Import completed")}: {added} added, {updated} updated.";await LoadAsync();
        }catch(Exception ex){status.Text=Tr.Loc.TranslateText("Import failed")+": "+ex.Message;}
    }

    private static string CsvEscape(string value) => "\"" + value.Replace("\"", "\"\"") + "\"";
    private static string[] ParseCsv(string line)
    {
        var result=new List<string>();var current=new StringBuilder();bool quoted=false;
        for(int i=0;i<line.Length;i++){char ch=line[i];if(ch=='"'){if(quoted&&i+1<line.Length&&line[i+1]=='"'){current.Append('"');i++;}else quoted=!quoted;}else if(ch==','&&!quoted){result.Add(current.ToString());current.Clear();}else current.Append(ch);}
        result.Add(current.ToString());return result.ToArray();
    }
}
