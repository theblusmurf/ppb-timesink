namespace PoteHunter;

internal sealed record RecognitionSelection(RepairPatch Marker,RepairPatch? Button,Point Click);

// This editor annotates a captured game image; it never clicks the game.
internal sealed class RepairSetupForm : Form
{
    readonly RepairCanvas canvas;
    readonly Label instruction=new(){AutoSize=true,MaximumSize=new Size(940,0),Padding=new Padding(8)};
    readonly Button next=new(){Text="Next: choose button",AutoSize=true,Enabled=false};
    readonly Bitmap image;
    RepairPatch? marker;
    internal bool PreviewOnly;
    protected override bool ShowWithoutActivation=>PreviewOnly;
    public RecognitionSelection? Selection {get;private set;}

    public RepairSetupForm(Bitmap image,bool confirmation,Color background,Color foreground)
        :this(image,background,foreground,confirmation?"Repair setup · confirmation":"Repair setup · inventory",
            confirmation?"Drag around distinctive, static REPAIR question text in the confirmation dialog. Exclude the gold amount and Yes/No buttons. Then choose Next."
                :"Drag around the inventory title or distinctive static text. Exclude item slots, gold, and the hammer. Then choose Next.",
            confirmation?"Click the center of the repair dialog's YES button in this image. Then choose Save selection."
                :"Click the center of the REPAIR HAMMER in this image. Then choose Save selection.",false) { }

    internal static RepairSetupForm ForRevival(Bitmap image,bool opening,Color background,Color foreground)=>new(image,background,foreground,
        opening?"Revival setup · open dialog":"Revival setup · confirm",
        opening?"Drag around distinctive, static death-screen text visible BEFORE the Revive dialog opens. Exclude timers and changing numbers. Then choose Next."
            :"Drag around distinctive, static text in the REVIVE dialog, separate from its button. Exclude timers and changing numbers. Then choose Next.",
        opening?"Click the place you normally click to OPEN the Revive dialog. This image editor sends no game input. Then choose Save selection."
            :"Click the center of the REVIVE button in this image. This image editor sends no game input. Then choose Save selection.",opening);

    RepairSetupForm(Bitmap image,Color background,Color foreground,string title,string markerInstruction,string buttonInstruction,bool pointOnly)
    {
        this.image=image;
        Text=title;
        next.Text=pointOnly?"Next: opening click":"Next: choose button";
        BackColor=background;ForeColor=foreground;Font=new Font("Segoe UI",10);
        Size=new(1050,760);MinimumSize=new(760,560);StartPosition=FormStartPosition.CenterParent;
        var layout=new TableLayoutPanel{Dock=DockStyle.Fill,ColumnCount=1,RowCount=3};
        layout.RowStyles.Add(new(SizeType.AutoSize));layout.RowStyles.Add(new(SizeType.Percent,100));layout.RowStyles.Add(new(SizeType.AutoSize));
        Controls.Add(layout);layout.Controls.Add(instruction,0,0);
        var scroll=new Panel{Dock=DockStyle.Fill,AutoScroll=true,BackColor=Color.Black};layout.Controls.Add(scroll,0,1);
        canvas=new RepairCanvas(image);scroll.Controls.Add(canvas);
        var zoom=new NumericUpDown{Minimum=10,Maximum=200,Increment=10,Value=100,Width=65};
        var cancel=new Button{Text="Cancel",DialogResult=DialogResult.Cancel,AutoSize=true};CancelButton=cancel;
        var reset=new Button{Text="Reselect marker",AutoSize=true};
        var controls=new FlowLayoutPanel{Dock=DockStyle.Fill,AutoSize=true,Padding=new Padding(8)};
        controls.Controls.AddRange([new Label{Text="Zoom %",AutoSize=true,Padding=new Padding(0,6,0,0)},zoom,reset,next,cancel]);layout.Controls.Add(controls,0,2);
        zoom.ValueChanged+=(_,_)=>canvas.SetZoom((double)zoom.Value/100);
        Shown+=(_,_)=>zoom.Value=Math.Clamp((decimal)(Math.Min((double)scroll.ClientSize.Width/image.Width,(double)scroll.ClientSize.Height/image.Height)*100),10,100);
        void MarkerInstruction()
        {
            instruction.Text=markerInstruction;
        }
        MarkerInstruction();
        reset.Click+=(_,_)=>{marker=null;canvas.ChoosePoint=false;canvas.Selected=Rectangle.Empty;next.Enabled=false;next.Text=pointOnly?"Next: opening click":"Next: choose button";MarkerInstruction();canvas.Invalidate();};
        canvas.SelectionChanged+=(_,_)=>next.Enabled=!canvas.Selected.IsEmpty;
        next.Click+=(_,_)=>
        {
            try
            {
                if(marker==null)
                {
                    marker=RepairPatch.Capture(image,canvas.Selected);canvas.ChoosePoint=true;canvas.Selected=Rectangle.Empty;
                    instruction.Text=buttonInstruction;
                    next.Text="Save selection";next.Enabled=false;canvas.Invalidate();return;
                }
                Point click=new(canvas.Selected.X+canvas.Selected.Width/2,canvas.Selected.Y+canvas.Selected.Height/2);
                if(!new Rectangle(Point.Empty,image.Size).Contains(click))throw new InvalidOperationException("Choose a point inside the game image.");
                var button=pointOnly?null:RepairPatch.Capture(image,canvas.Selected);
                if(button!=null && marker.Bounds.IntersectsWith(button.Bounds))throw new InvalidOperationException("The recognition text must be separate from the button. Reselect the marker.");
                Selection=new(marker,button,click);DialogResult=DialogResult.OK;
            }
            catch(Exception ex){instruction.Text=ex.Message;}
        };
    }

    sealed class RepairCanvas : Control
    {
        readonly Bitmap image;
        double zoom=1;
        Point start;
        bool dragging;
        public bool ChoosePoint;
        [System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)]
        public Rectangle Selected {get;set;}
        public event EventHandler? SelectionChanged;
        public RepairCanvas(Bitmap image){this.image=image;DoubleBuffered=true;Cursor=Cursors.Cross;SetZoom(1);}
        public void SetZoom(double value){zoom=value;Size=new((int)(image.Width*zoom),(int)(image.Height*zoom));Invalidate();}
        Point ImagePoint(Point p)=>new(Math.Clamp((int)(p.X/zoom),0,image.Width-1),Math.Clamp((int)(p.Y/zoom),0,image.Height-1));
        protected override void OnPaint(PaintEventArgs e)
        {
            e.Graphics.DrawImage(image,ClientRectangle);
            if(!Selected.IsEmpty)
            {
                using var pen=new Pen(Color.Cyan,2);
                e.Graphics.DrawRectangle(pen,(float)(Selected.X*zoom),(float)(Selected.Y*zoom),(float)(Selected.Width*zoom),(float)(Selected.Height*zoom));
            }
        }
        protected override void OnMouseDown(MouseEventArgs e)
        {
            if(e.Button!=MouseButtons.Left)return;
            start=ImagePoint(e.Location);
            if(ChoosePoint)
            {
                Selected=new(start.X-20,start.Y-12,40,24);SelectionChanged?.Invoke(this,EventArgs.Empty);Invalidate();return;
            }
            dragging=true;Capture=true;Selected=Rectangle.Empty;
        }
        protected override void OnMouseMove(MouseEventArgs e)
        {
            if(!dragging)return;
            var end=ImagePoint(e.Location);
            Selected=Rectangle.FromLTRB(Math.Min(start.X,end.X),Math.Min(start.Y,end.Y),Math.Max(start.X,end.X),Math.Max(start.Y,end.Y));Invalidate();
        }
        protected override void OnMouseUp(MouseEventArgs e)
        {
            if(!dragging)return;OnMouseMove(e);dragging=false;Capture=false;SelectionChanged?.Invoke(this,EventArgs.Empty);
        }
    }
}
