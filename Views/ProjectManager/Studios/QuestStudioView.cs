using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Xml.Linq;
using Vector2LevelEditor.Models.ProjectManager;
using Vector2LevelEditor.Services.ProjectManager;

namespace Vector2LevelEditor.Views.ProjectManager.Studios;

public sealed class QuestStudioView : UserControl
{
    private sealed class StepEdit
    {
        public string Title = "";
        public string Event = "";
        public string VisualGroup = "";
        public string VisualGif = "";
    }

    private readonly string _root;
    private readonly string _folder;
    private readonly Action<string> _status;
    private readonly ListBox _library = new();
    private readonly TextBox _search = StudioUi.Field();
    private readonly StackPanel _editor = new();
    private readonly List<StepEdit> _steps = [];
    private readonly Dictionary<string, TextBox> _fields = [];
    private XDocument? _document;
    private string? _path;
    private string _start = "When the menu opens";
    private string _reward = "None";

    public QuestStudioView(string root, Action<string> status)
    {
        _root=root; _folder=Path.Combine(root,"custom_quests"); _status=status;
        var body=new Grid();
        body.ColumnDefinitions.Add(new ColumnDefinition { Width=new GridLength(250) });
        body.ColumnDefinitions.Add(new ColumnDefinition());
        var left=new DockPanel();
        var find=StudioUi.Search(_search,"Find a quest"); find.Margin=new Thickness(12); DockPanel.SetDock(find,Dock.Top); left.Children.Add(find);
        _search.TextChanged += (_,_) => Reload(_path);
        _library.SelectionChanged += (_,_) => LoadSelected();
        left.Children.Add(_library);
        body.Children.Add(new Border { Child=left, BorderBrush=StudioUi.Resource("ProjectManagerEdgeBrush"), BorderThickness=new Thickness(0,0,1,0) });
        var scroll=new ScrollViewer { Content=_editor, VerticalScrollBarVisibility=ScrollBarVisibility.Auto };
        Grid.SetColumn(scroll,1); body.Children.Add(scroll);
        Content=StudioUi.Shell("Quest Designer","Build the quest start, ordered objectives, and reward.",
            "\uE8A4",new SolidColorBrush(Color.FromRgb(78,117,205)),body,
            StudioUi.Button("Reload",(_,_) => Reload(_path)),
            StudioUi.Button("New Quest",(_,_) => Create(),true));
        Reload();
    }

    private void Reload(string? selected=null)
    {
        Directory.CreateDirectory(_folder);
        var files=Directory.EnumerateFiles(_folder,"*.xml").Where(file => {
            try {
                var item=XDocument.Load(file).Descendants("Quest").FirstOrDefault();
                return item is not null && (((string?)item.Element("Info")?.Element("VisualName")?.Attribute("Value") ?? "").Contains(_search.Text,StringComparison.OrdinalIgnoreCase)
                    || ((string?)item.Attribute("Name") ?? "").Contains(_search.Text,StringComparison.OrdinalIgnoreCase));
            } catch { return false; }
        }).OrderBy(Path.GetFileName).ToList();
        _library.ItemsSource=files;
        _library.ItemTemplate=LibraryTemplate();
        _library.SelectedItem=selected is not null && files.Contains(selected) ? selected : files.FirstOrDefault();
        if(files.Count==0) {
            _editor.Children.Clear();
            _editor.Children.Add(new TextBlock { Text="No quests yet", FontSize=16, FontWeight=FontWeights.SemiBold, Margin=new Thickness(30,90,0,14) });
            var create = StudioUi.Button("New Quest",(_,_) => Create(),true);
            create.HorizontalAlignment = HorizontalAlignment.Left;
            create.Margin = new Thickness(30, 0, 0, 0);
            _editor.Children.Add(create);
        }
    }
    private static DataTemplate LibraryTemplate()
    {
        var template=new DataTemplate();
        var factory=new FrameworkElementFactory(typeof(TextBlock));
        factory.SetBinding(TextBlock.TextProperty,new System.Windows.Data.Binding { Converter=new QuestTitleConverter() });
        factory.SetValue(TextBlock.PaddingProperty,new Thickness(8,9,8,9)); template.VisualTree=factory; return template;
    }
    private sealed class QuestTitleConverter : System.Windows.Data.IValueConverter
    {
        public object Convert(object value,Type targetType,object parameter,System.Globalization.CultureInfo culture)
        {
            try { var item=XDocument.Load((string)value).Descendants("Quest").First(); return (string?)item.Element("Info")?.Element("VisualName")?.Attribute("Value") ?? Path.GetFileNameWithoutExtension((string)value); }
            catch { return Path.GetFileNameWithoutExtension((string)value); }
        }
        public object ConvertBack(object value,Type targetType,object parameter,System.Globalization.CultureInfo culture) => throw new NotSupportedException();
    }
    private void Create()
    {
        try {
            var name="NewQuest";
            for(var i=2;File.Exists(Path.Combine(_folder,name+".xml"));i++) name="NewQuest"+i;
            var path=new ProjectTemplateService().Create(new Vector2Project { RootPath=_root },ProjectSection.All.Single(section=>section.Name=="Quests"),name);
            Reload(path);
            _status("Created quest " + name + ".");
        } catch(Exception ex) { _status(ex.Message); }
    }
    private void LoadSelected()
    {
        _path=_library.SelectedItem as string; _document=null; _editor.Children.Clear(); _fields.Clear(); _steps.Clear();
        if(_path is null) return;
        try {
            _document=XDocument.Load(_path,LoadOptions.PreserveWhitespace);
            var quest=_document.Descendants("Quest").FirstOrDefault() ?? throw new InvalidDataException("Missing Quests/Quest.");
            var previous=quest.Descendants("CounterRange").FirstOrDefault(node=>(string?)node.Attribute("Namespace")=="ST_Quests" && (string?)node.Attribute("Equal")=="-1");
            var startEvent=quest.Element("StartTrigger")?.Descendants("Events").FirstOrDefault()?.Elements().FirstOrDefault();
            var message=(string?)startEvent?.Attribute("Message") ?? "";
            _start=previous is not null ? "After another quest" : startEvent?.Name.LocalName=="OnScreen" ? "When the menu opens" :
                message.StartsWith("Content.ZoneSelected",StringComparison.Ordinal) ? "When a zone is selected" :
                message.StartsWith("Content.ChapterStart",StringComparison.Ordinal) ? "When a chapter starts" :
                message.StartsWith("Content.FloorStart",StringComparison.Ordinal) ? "When a floor starts" : "From a custom event";
            var reference=previous is not null ? (string?)previous.Attribute("Name") ?? "" : message.Contains(':') ? message[(message.IndexOf(':')+1)..] : "";
            foreach(var trigger in quest.Elements("Trigger").Where(node=>(string?)node.Attribute("EditorManaged")=="Sequence"))
            {
                var eventName=(string?)trigger.Descendants("OnCall").FirstOrDefault()?.Attribute("Message") ?? "";
                var visual=(string?)trigger.Descendants("ExecuteCall").FirstOrDefault()?.Attribute("Message") ?? "";
                var parts=visual.Split(':');
                _steps.Add(new StepEdit { Title=(string?)trigger.Attribute("EditorTitle") ?? "Objective", Event=eventName,
                    VisualGroup=parts.Length>=3 ? parts[1] : "", VisualGif=parts.Length>=3 ? parts[2].Replace("gif_","").Replace(".xml",".gif") : "" });
            }
            if(_steps.Count==0) _steps.Add(new StepEdit { Title="Reach the objective", Event="QuestGoalReached" });
            _reward=(string?)quest.Element("Info")?.Element("Reward")?.Attribute("Type") ?? "None";
            Render(quest,reference);
        } catch(Exception ex) { _status(ex.Message); }
    }
    private TextBox Field(Panel panel,string key,string label,string value,bool multiline=false)
    {
        panel.Children.Add(StudioUi.Label(label));
        var input=StudioUi.Field(value);
        if(multiline) { input.Height=64; input.AcceptsReturn=true; input.TextWrapping=TextWrapping.Wrap; }
        _fields[key]=input; panel.Children.Add(input); return input;
    }
    private static ComboBox Picker(Panel panel,string label,string current,string[] choices,Action<string> change)
    {
        panel.Children.Add(StudioUi.Label(label));
        var picker=new ComboBox { ItemsSource=choices.Append(current).Distinct().ToArray(), SelectedItem=current, Margin=new Thickness(0,4,0,12) };
        picker.SelectionChanged += (_,_) => change(picker.SelectedItem as string ?? ""); panel.Children.Add(picker); return picker;
    }
    private void Render(XElement quest,string reference)
    {
        _editor.Children.Clear(); _fields.Clear();
        var panel=new StackPanel { MaxWidth=900, Margin=new Thickness(28,22,28,30) };
        panel.Children.Add(new TextBlock { Text=(string?)quest.Element("Info")?.Element("VisualName")?.Attribute("Value") ?? "Quest", FontSize=22, FontWeight=FontWeights.Bold, Margin=new Thickness(0,0,0,18) });
        panel.Children.Add(StudioUi.Heading("Basics","\uE70F",Color.FromRgb(49,128,213)));
        Field(panel,"Id","Quest ID",(string?)quest.Attribute("Name") ?? "");
        Field(panel,"Name","Display name",(string?)quest.Element("Info")?.Element("VisualName")?.Attribute("Value") ?? "");
        Field(panel,"Description","Description",(string?)quest.Element("Info")?.Element("Description")?.Attribute("Value") ?? "",true);
        panel.Children.Add(StudioUi.Heading("Quest Start","\uE768",Color.FromRgb(46,166,101)));
        Picker(panel,"Start",_start,["When the menu opens","After another quest","When a zone is selected","When a chapter starts","When a floor starts","From a custom event"],value=>_start=value);
        Field(panel,"Reference","Previous quest ID, zone, chapter, floor or event",reference);
        panel.Children.Add(StudioUi.Heading("Level Sequence","\uE8EF",Color.FromRgb(100,87,204)));
        var stepsPanel=new StackPanel(); panel.Children.Add(stepsPanel);
        void RenderSteps()
        {
            stepsPanel.Children.Clear();
            for(var index=0;index<_steps.Count;index++)
            {
                var step=_steps[index]; var number=index;
                var block=new StackPanel { Margin=new Thickness(0,0,0,14) };
                var bar=new DockPanel { LastChildFill=false };
                bar.Children.Add(new TextBlock { Text="Objective "+(index+1), FontWeight=FontWeights.SemiBold, VerticalAlignment=VerticalAlignment.Center });
                foreach(var direction in new[] {-1,1})
                {
                    var move=StudioUi.Button(direction<0 ? "\uE70E" : "\uE70D",(_,_) => {
                        var destination=number+direction; if(destination<0 || destination>=_steps.Count) return;
                        (_steps[number],_steps[destination])=(_steps[destination],_steps[number]); RenderSteps();
                    }); move.FontFamily=new FontFamily("Segoe MDL2 Assets"); move.ToolTip=direction<0 ? "Move earlier" : "Move later";
                    DockPanel.SetDock(move,Dock.Right); bar.Children.Add(move);
                }
                var remove=StudioUi.Button("\uE74D",(_,_) => { if(_steps.Count<=1) return; _steps.Remove(step); RenderSteps(); });
                remove.FontFamily=new FontFamily("Segoe MDL2 Assets"); remove.ToolTip="Remove objective";
                DockPanel.SetDock(remove,Dock.Right); bar.Children.Add(remove); block.Children.Add(bar);
                void StepField(string label,string current,Action<string> set) {
                    block.Children.Add(StudioUi.Label(label)); var input=StudioUi.Field(current); input.TextChanged += (_,_) => set(input.Text); block.Children.Add(input);
                }
                StepField("Objective shown to the player",step.Title,value=>step.Title=value);
                StepField("Trigger event ID",step.Event,value=>step.Event=value);
                block.Children.Add(StudioUi.Button("Copy Trigger XML",(_,_) => {
                    var trigger=new XElement("Trigger",new XAttribute("Name","Quest_"+ProjectTemplateService.SafeId(step.Event)),new XAttribute("X","0"),new XAttribute("Y","0"),new XAttribute("Width","320"),new XAttribute("Height","180"),
                        new XElement("Content",new XElement("Loop",new XElement("Events",new XElement("Enter")),
                            new XElement("Actions",new XElement("ExecuteCall",new XAttribute("Message",step.Event))))));
                    Clipboard.SetText(trigger.ToString()); _status("Copied the room trigger for "+step.Event+".");
                }));
                StepField("Animated visual ID (optional)",step.VisualGroup,value=>step.VisualGroup=value);
                StepField("GIF artwork (optional)",step.VisualGif,value=>step.VisualGif=value);
                stepsPanel.Children.Add(new Border { Background=new SolidColorBrush(Color.FromArgb(13,107,90,211)), CornerRadius=new CornerRadius(8), Padding=new Thickness(14), Child=block, Margin=new Thickness(0,0,0,6) });
            }
        }
        RenderSteps();
        panel.Children.Add(StudioUi.Button("+ Add Objective",(_,_) => { _steps.Add(new StepEdit { Title="New objective",Event="QuestEvent"+(_steps.Count+1) }); RenderSteps(); },true));
        panel.Children.Add(StudioUi.Heading("Reward","\uF133",Color.FromRgb(219,145,42)));
        Picker(panel,"Reward type",_reward,["None","CustomReward","Preset"],value=>_reward=value);
        Field(panel,"RewardId","Custom reward or native preset ID",(string?)quest.Element("Info")?.Element("Reward")?.Attribute("Name") ?? "");
        Field(panel,"RewardName","Reward label",(string?)quest.Element("Info")?.Element("Reward")?.Attribute("VisualName") ?? "");
        panel.Children.Add(StudioUi.Label("Reward image"));
        var image=StudioUi.Field((string?)quest.Element("Info")?.Element("Reward")?.Attribute("ImageName") ?? ""); _fields["RewardImage"]=image;
        panel.Children.Add(new ProjectArtworkField(_root,image));
        var advanced=new Expander { Header="Advanced Quest XML", Margin=new Thickness(0,24,0,10) };
        var rawBody=new StackPanel();
        var raw=StudioUi.Field(_document?.ToString() ?? ""); raw.AcceptsReturn=true; raw.AcceptsTab=true; raw.Height=260; raw.FontFamily=new FontFamily("Consolas"); raw.VerticalScrollBarVisibility=ScrollBarVisibility.Auto; raw.HorizontalScrollBarVisibility=ScrollBarVisibility.Auto;
        rawBody.Children.Add(raw);
        rawBody.Children.Add(StudioUi.Button("Validate & Apply",(_,_) => {
            try {
                var parsed=XDocument.Parse(raw.Text,LoadOptions.PreserveWhitespace);
                if(parsed.Root?.Name.LocalName!="Quests" || parsed.Descendants("Quest").FirstOrDefault() is not { } value || string.IsNullOrWhiteSpace((string?)value.Attribute("Name")) || value.Element("StartTrigger")?.Element("Content") is null)
                    throw new InvalidDataException("Expected Quests/Quest with Name and StartTrigger/Content.");
                var temporary=_path+".tmp"; parsed.Save(temporary); File.Move(temporary,_path!,true);
                _status("Applied native quest XML."); Reload(_path);
            } catch(Exception ex) { _status("Quest XML: "+ex.Message); }
        },true));
        advanced.Content=rawBody; panel.Children.Add(advanced);
        var actions=new StackPanel { Orientation=Orientation.Horizontal, HorizontalAlignment=HorizontalAlignment.Right, Margin=new Thickness(0,12,0,0) };
        actions.Children.Add(StudioUi.Button("Delete Quest",(_,_) => Delete()));
        actions.Children.Add(StudioUi.Button("Revert",(_,_) => LoadSelected()));
        actions.Children.Add(StudioUi.Button("Save Quest",(_,_) => Save(),true)); panel.Children.Add(actions);
        _editor.Children.Add(panel);
    }

    private void Save()
    {
        if(_document?.Descendants("Quest").FirstOrDefault() is not { } quest || _path is null) return;
        try {
            var id=ProjectTemplateService.SafeId(_fields["Id"].Text);
            if(id.Length==0 || string.IsNullOrWhiteSpace(_fields["Name"].Text)) throw new InvalidDataException("Quest ID and display name are required.");
            if(_steps.Count==0 || _steps.Any(step=>string.IsNullOrWhiteSpace(step.Event) || string.IsNullOrWhiteSpace(step.Title))) throw new InvalidDataException("Every objective needs a title and trigger event ID.");
            var info=quest.Element("Info") ?? new XElement("Info"); if(info.Parent is null) quest.AddFirst(info);
            var visual=info.Element("VisualName") ?? new XElement("VisualName"); if(visual.Parent is null) info.Add(visual);
            var description=info.Element("Description") ?? new XElement("Description"); if(description.Parent is null) info.Add(description);
            var reward=info.Element("Reward") ?? new XElement("Reward"); if(reward.Parent is null) info.Add(reward);
            quest.SetAttributeValue("Name",id);
            visual.SetAttributeValue("Value",_fields["Name"].Text.Trim());
            description.SetAttributeValue("Value",_fields["Description"].Text.Trim());
            description.SetAttributeValue("Progress",(string?)description.Attribute("Progress") ?? "");
            reward.SetAttributeValue("Type",_reward); reward.SetAttributeValue("Name",_fields["RewardId"].Text.Trim());
            reward.SetAttributeValue("VisualName",_fields["RewardName"].Text.Trim()); reward.SetAttributeValue("ImageName",_fields["RewardImage"].Text.Trim());
            quest.Elements().Where(node=>((string?)node.Attribute("EditorManaged")) is "1" or "Sequence").Remove();
            quest.Add(QuestSequenceCompiler.StartTrigger(id,_start,_fields["Reference"].Text));
            quest.Add(QuestSequenceCompiler.ProgressTriggers(id,_steps.Select(step=>new QuestStep(step.Title,step.Event,step.VisualGroup,step.VisualGif)).ToArray(),
                _reward=="None" ? "" : _fields["RewardId"].Text));
            var temporary=_path+".tmp"; _document.Save(temporary); File.Move(temporary,_path,true);
            _status("Saved quest "+id+"."); Reload(_path);
        } catch(Exception ex) { _status(ex.Message); }
    }
    private void Delete()
    {
        if(_path is null || MessageBox.Show("Delete this quest?","Delete",MessageBoxButton.YesNo,MessageBoxImage.Warning)!=MessageBoxResult.Yes) return;
        try { File.Delete(_path); Reload(); _status("Deleted quest."); } catch(Exception ex) { _status(ex.Message); }
    }
}
