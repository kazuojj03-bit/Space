using TMPro;
using Il2CppInterop.Runtime;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.UI;
using Object = UnityEngine.Object;

namespace SpacePhone;

// Uses the game's uGUI/TMP stack. Does not depend on stripped IMGUI or OS font methods.
internal sealed class PhoneView
{
    private sealed class Element
    {
        public string Kind = "";
        public GameObject Object = null!;
        public RectTransform Rect = null!;
        public Image? Image;
        public TextMeshProUGUI? Text;
        public Button? Button;
        public string Key="";
        public Color BaseColor=Color.white;
        public float PressedUntil;
    }
    private readonly List<Element> pool = new();
    private readonly Dictionary<string,Sprite> assets = new();
    private readonly List<Texture2D> textures = new();
    private readonly TMP_FontAsset font;
    private readonly GameObject canvasObject;
    private readonly Canvas canvas;
    private readonly RectTransform panel;
    private int cursor;
    private float slide;
    private readonly List<(Rect Area,Action Action)> hits=new();
    public Action? Feedback;
    public PhoneView(GameObject parent)
    {
        font = FontLoader.Instance.simplifiedChineseFont ?? FontLoader.Instance.notoSans;
        if (font == null) throw new InvalidOperationException("Game font is not ready");
        canvasObject = new GameObject("SpacePhone Canvas");
        canvasObject.transform.SetParent(parent.transform,false);
        canvas = Add<Canvas>(canvasObject);
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 32000;
        var scaler = Add<CanvasScaler>(canvasObject);
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920,1080);
        scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
        scaler.matchWidthOrHeight = 1;
        Add<GraphicRaycaster>(canvasObject);
        var phone = new GameObject("SpacePhone Pixel Terminal");
        panel = Add<RectTransform>(phone);
        panel.SetParent(canvasObject.transform,false);
        panel.anchorMin = panel.anchorMax = new Vector2(1,0);
        panel.pivot = new Vector2(1,0);
        panel.anchoredPosition = new Vector2(-32,32);
        panel.sizeDelta = new Vector2(440,850);
        var background = Add<Image>(phone);
        background.sprite = Asset("device"); background.raycastTarget = true;
        canvasObject.SetActive(false);
    }
    private Sprite Asset(string name)
    {
        if(assets.TryGetValue(name,out var sprite))return sprite;
        string path=Path.Combine(BepInEx.Paths.PluginPath,"SpacePhone","Assets",name+".png");
        var texture=new Texture2D(2,2,TextureFormat.RGBA32,false);
        if(!ImageConversion.LoadImage(texture,File.ReadAllBytes(path),false))throw new InvalidDataException("Cannot load "+path);
        texture.filterMode=FilterMode.Point;
        var border=name=="rounded" ? new Vector4(16,16,16,16) : Vector4.zero;
        sprite=Sprite.Create(texture,new Rect(0,0,texture.width,texture.height),new Vector2(.5f,.5f),100,0,SpriteMeshType.FullRect,border,false);
        assets[name]=sprite;textures.Add(texture);return sprite;
    }
    public void Show(bool show)
    {
        if(show)FollowNativeFloatingLayer();
        if(show && !canvasObject.activeSelf) { slide=0;panel.anchoredPosition=new Vector2(-32,-870); }
        canvasObject.SetActive(show);
    }
    public void Tick(float delta)
    {
        if(!canvasObject.activeSelf)return;
        if(slide<1){slide=Math.Min(1,slide+delta/0.22f);float ease=1-Mathf.Pow(1-slide,3);panel.anchoredPosition=new Vector2(-32,Mathf.Lerp(-870,32,ease));}
        if(NativePointer.Position(out var point))
        {
            var corners=new Il2CppInterop.Runtime.InteropTypes.Arrays.Il2CppStructArray<Vector3>(4);panel.GetWorldCorners(corners);
            float width=corners[2].x-corners[0].x,height=corners[2].y-corners[0].y;if(width<=0||height<=0)return;
            var local=new Vector2((point.x-corners[0].x)/width*440,(corners[2].y-point.y)/height*850);
            foreach(var e in pool.Where(e=>e.Button!=null && e.Object.activeInHierarchy))
            {
                var area=new Rect(e.Rect.anchoredPosition.x,-e.Rect.anchoredPosition.y,e.Rect.sizeDelta.x,e.Rect.sizeDelta.y);
                bool hover=area.Contains(local)&&e.Button!.interactable;
                e.Image!.color=e.BaseColor*(hover?1.18f:1f);
                float scale=Time.unscaledTime<e.PressedUntil?.965f:1f;e.Rect.localScale=new Vector3(scale,scale,1);
            }
        }
    }
    private static T Add<T>(GameObject obj) where T:Component => obj.AddComponent(Il2CppType.Of<T>()).Cast<T>();
    internal bool OwnsSurface(GameObject? target) => target != null &&
        (target.Pointer == canvasObject.Pointer || target.transform.IsChildOf(canvasObject.transform));
    internal void FollowNativeFloatingLayer()
    {
        // UICapture deliberately uses WorldSpace and its own camera. Respect
        // that temporary mode; normal phone rendering uses the game's existing
        // floating-window camera without changing any native camera settings.
        if(canvas.renderMode==RenderMode.WorldSpace)return;
        var camera=LetterboxManager.Instance?.dockCamera;
        if(camera==null)return;
        if(canvas.renderMode!=RenderMode.ScreenSpaceCamera)canvas.renderMode=RenderMode.ScreenSpaceCamera;
        if(canvas.worldCamera==null||canvas.worldCamera.Pointer!=camera.Pointer)canvas.worldCamera=camera;
        if(canvas.sortingOrder!=32000)canvas.sortingOrder=32000;
    }
    public void Begin() { cursor=0;hits.Clear();Show(true); }
    public bool ClickLocal(float x,float y) {
        for(int i=hits.Count-1;i>=0;i--)if(hits[i].Area.Contains(new Vector2(x,y))) {Feedback?.Invoke();foreach(var e in pool.Where(e=>e.Button!=null&&e.Object.activeInHierarchy)){var area=new Rect(e.Rect.anchoredPosition.x,-e.Rect.anchoredPosition.y,e.Rect.sizeDelta.x,e.Rect.sizeDelta.y);if(area.Contains(new Vector2(x,y)))e.PressedUntil=Time.unscaledTime+.12f;}hits[i].Action();return true;}return false;
    }
    public void ProcessPointer() {
        if(!canvasObject.activeSelf || !NativePointer.Click(out var point))return;
        DispatchScreen(point);
    }
    public Vector2 ScreenPosition(float x,float y)
    {
        var corners=new Il2CppInterop.Runtime.InteropTypes.Arrays.Il2CppStructArray<Vector3>(4);panel.GetWorldCorners(corners);
        return new Vector2(Mathf.Lerp(corners[0].x,corners[2].x,x/440),Mathf.Lerp(corners[2].y,corners[0].y,y/850));
    }
    public bool DispatchScreen(Vector2 point)
    {
        var corners=new Il2CppInterop.Runtime.InteropTypes.Arrays.Il2CppStructArray<Vector3>(4);panel.GetWorldCorners(corners);
        float width=corners[2].x-corners[0].x,height=corners[2].y-corners[0].y;
        if(width<=0 || height<=0)return false;
        float x=(point.x-corners[0].x)/width*440,y=(corners[2].y-point.y)/height*850;
        bool hit=ClickLocal(x,y);
        if(Plugin.Instance!=null)Plugin.Instance.Log.LogInfo($"Phone pointer screen={point}, local=({x:F1},{y:F1}), hit={hit}");
        return hit;
    }
    public void End() { for(int i=cursor;i<pool.Count;i++)pool[i].Object.SetActive(false); }
    private Element Get(string kind,float x,float y,float w,float h)
    {
        Element element;
        if(cursor<pool.Count && pool[cursor].Kind==kind) element=pool[cursor];
        else
        {
            if(cursor<pool.Count)Object.Destroy(pool[cursor].Object);
            var go=new GameObject("SpacePhone "+kind+" "+cursor);go.layer=5;
            var rect=Add<RectTransform>(go);rect.SetParent(panel,false);
            rect.anchorMin=rect.anchorMax=new Vector2(0,1);rect.pivot=new Vector2(0,1);
            element=new Element{Kind=kind,Object=go,Rect=rect};
            if(kind=="label") { element.Text=Add<TextMeshProUGUI>(go); element.Text.font=font; element.Text.raycastTarget=false; element.Text.enableWordWrapping=true; }
            else { element.Image=Add<Image>(go); element.Image.raycastTarget=kind=="button" || kind=="icon"; }
            if(kind=="button" || kind=="icon")
            {
                element.Button=Add<Button>(go);element.Button.targetGraphic=element.Image;
                element.Button.transition=Selectable.Transition.None;
            }
            if(cursor<pool.Count)pool[cursor]=element;else pool.Add(element);
        }
        cursor++;element.Object.SetActive(true);element.Rect.anchoredPosition=new Vector2(x,-y);element.Rect.sizeDelta=new Vector2(w,h);
        element.Rect.SetAsLastSibling();return element;
    }
    public void Fill(float x,float y,float w,float h,Color color,bool rounded=false)
    {
        var e=Get("image",x,y,w,h);e.Image!.color=color;e.Image.sprite=rounded?Asset("rounded"):null;e.Image.type=rounded?Image.Type.Sliced:Image.Type.Simple;
    }
    public void Surface(float x,float y,float w,float h,string key,Action action,Color color,bool enabled=true)
    {var e=Get("button",x,y,w,h);e.Image!.sprite=Asset("rounded");e.Image.type=Image.Type.Sliced;e.Image.color=color*(enabled?1:.4f);e.BaseColor=e.Image.color;e.Key=key;e.Button!.interactable=enabled;e.Button.onClick.RemoveAllListeners();if(enabled)hits.Add((new Rect(x,y,w,h),action));}
    public void ClearHits()=>hits.Clear();
    public void Label(float x,float y,float w,float h,string value,bool large=false,bool centered=false,int size=0)
    {
        var e=Get("label",x,y,w,h);var label=e.Text!;
        float preferred=size>0?size:large?24:16;
        label.text=value;label.enableAutoSizing=true;label.fontSizeMin=preferred*.72f;label.fontSizeMax=preferred;label.fontSize=preferred;label.color=new Color(.82f,.86f,.73f);
        label.alignment=centered?TextAlignmentOptions.Center:TextAlignmentOptions.TopLeft;
        label.fontStyle=large?FontStyles.Bold:FontStyles.Normal;
        label.overflowMode=TextOverflowModes.Ellipsis;
    }
    public void Button(float x,float y,float w,float h,string value,Action action,bool enabled)
    {
        var e=Get("button",x,y,w,h);e.Image!.sprite=Asset("rounded");e.Image.type=Image.Type.Sliced;e.Image.color=new Color(.16f,.28f,.29f,.95f);
        e.Image.color*=enabled?1:.45f;e.Key=value;e.BaseColor=e.Image.color;
        e.Button!.interactable=enabled;e.Button.onClick.RemoveAllListeners();if(enabled)hits.Add((new Rect(x,y,w,h),action));
        Label(x+8,y+3,w-16,h-6,value,false,true);
    }
    public void Icon(float x,float y,float size,string asset,Action action)
    {
        var e=Get("icon",x,y,size,size);e.Image!.sprite=Asset(asset);e.Image.type=Image.Type.Simple;e.Image.color=Color.white;
        e.Key=asset;e.BaseColor=new Color(.86f,.9f,.84f);e.Image.color=e.BaseColor;
        e.Button!.interactable=true;e.Button.onClick.RemoveAllListeners();hits.Add((new Rect(x,y,size,size),action));
    }
    public void Scale(float factor)=>panel.localScale=new Vector3(factor,factor,1);
    public Rect ScreenArea(float x,float y,float w,float h)
    {var a=ScreenPosition(x,y);var b=ScreenPosition(x+w,y+h);return new Rect(a.x,b.y,b.x-a.x,a.y-b.y);}
    public void Card(float x,float y,float w,float h,string title,string value,string hint,Color accent)
    {Fill(x,y,w,h,new Color(.065f,.14f,.16f,.97f));Fill(x,y,3,h,accent);Label(x+13,y+9,w-26,25,title,false,false,13);Label(x+13,y+35,w-26,43,value,true,false,26);if(h>82)Label(x+13,y+h-26,w-26,23,hint,false,false,13);}
    public void Line(float x1,float y1,float x2,float y2,Color color,float thickness=2)
    {float dx=x2-x1,dy=y2-y1;float length=Mathf.Sqrt(dx*dx+dy*dy);var e=Get("line",x1,y1,length,thickness);e.Image!.sprite=null;e.Image.color=color;e.Rect.localRotation=Quaternion.Euler(0,0,-Mathf.Atan2(dy,dx)*Mathf.Rad2Deg);}
    internal Vector2 RenderedCenter(string key)
    {
        var e=pool.FirstOrDefault(e=>e.Key==key && e.Object.activeInHierarchy)??throw new InvalidOperationException("No visible control "+key);
        var corners=new Il2CppInterop.Runtime.InteropTypes.Arrays.Il2CppStructArray<Vector3>(4);e.Rect.GetWorldCorners(corners);
        return new Vector2((corners[0].x+corners[2].x)*.5f,(corners[0].y+corners[2].y)*.5f);
    }
    public void Destroy()
    {
        Object.Destroy(canvasObject);
        foreach(var sprite in assets.Values)Object.Destroy(sprite);
        foreach(var texture in textures)Object.Destroy(texture);
    }
}
