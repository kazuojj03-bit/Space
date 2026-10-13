using Il2CppInterop.Runtime;
using UnityEngine;
using Object=UnityEngine.Object;
namespace SpacePhone;

internal sealed class WorldPhone
{
    private readonly List<(SpriteRenderer Original,GameObject Model)> replacements=new();
    private readonly List<Mesh> meshes=new();
    private readonly List<Material> materials=new();
    private readonly List<Texture2D> textures=new();
    private static readonly Color Ink=new(.045f,.095f,.105f),Glass=new(.075f,.20f,.225f),Cream=new(.86f,.84f,.65f),Brass=new(.62f,.57f,.39f),Teal=new(.32f,.58f,.55f);

    public void Refresh()
    {
        foreach(var phone in Object.FindObjectsOfType<StorePhoneObject>())
        {
            var original=phone.GetComponent<SpriteRenderer>();
            var collider=phone.GetComponent<BoxCollider2D>();
            if(original==null || collider==null || replacements.Any(r=>r.Original!=null && r.Original.Pointer==original.Pointer))continue;
            var area=collider.bounds;
            if(area.size.x<=0 || area.size.y<=0)continue;
            GameObject? model=null;
            try
            {
                model=CreateModel();model.transform.SetParent(phone.transform,false);
                model.transform.position=new Vector3(area.center.x,area.center.y,original.transform.position.z-.02f);
                model.transform.rotation=Quaternion.Euler(24,-15,67);
                // Bound the handset itself. The original sprite contains transparent pixels spanning the entire scene.
                var renderers=model.GetComponentsInChildren<MeshRenderer>();
                if(renderers.Length==0)throw new InvalidOperationException("Phone mesh has no renderers");
                var bounds=renderers[0].bounds;
                foreach(var renderer in renderers)bounds.Encapsulate(renderer.bounds);
                float scale=.80f*Math.Min(area.size.x/Math.Max(.001f,bounds.size.x),area.size.y/Math.Max(.001f,bounds.size.y));
                model.transform.localScale=model.transform.localScale*scale;
                foreach(var renderer in renderers)
                {
                    renderer.gameObject.layer=original.gameObject.layer;
                    renderer.sortingLayerID=original.sortingLayerID;
                    // Shared order lets depth and distance sort the front, sides and rear instead of overlaying the rear camera.
                    renderer.sortingOrder=original.sortingOrder;
                }
                replacements.Add((original,model));original.enabled=false;
                Plugin.Instance.Log.LogInfo($"Detailed 3D phone fitted to collider: center={area.center}, size={area.size}, scale={scale}");
            }
            catch { if(model!=null)Object.Destroy(model);throw; }
        }
    }

    public GameObject CreateModel()
    {
        int meshStart=meshes.Count,materialStart=materials.Count,textureStart=textures.Count;
        var root=new GameObject("SpacePhone 3D handset");
        try
        {
            var paint=new Paint(this,root);
            Chassis(paint);
            Plane(paint,"Inset glass gasket",0,0,.865f,1.64f,.085f,-.064f,Ink);
            Plane(paint,"Glass screen",0,0,.825f,1.565f,.069f,-.068f,Glass);
            // Small pixel graphics belong to the handset mesh and remain readable in the game's illustrated palette.
            var screen=new Shape();
            screen.Quad(-.365f,.557f,.73f,.003f,-.070f,Shade(Teal,.65f));
            Glyph(screen,"09:41",-.355f,.687f,.011f,-.072f,Cream);
            Glyph(screen,"ORBIT",-.355f,.593f,.009f,-.072f,Teal);
            for(int i=0;i<3;i++)screen.Quad(.223f+i*.023f,.681f,.013f,.016f+i*.014f,-.072f,Cream);
            screen.Quad(.313f,.685f,.047f,.025f,-.072f,Cream);screen.Quad(.362f,.692f,.006f,.01f,-.072f,Cream);
            screen.Quad(.321f,.691f,.030f,.013f,-.074f,Glass);
            // Fine reflections, subdued so the icons retain their silhouettes.
            screen.Quad(-.398f,-.68f,.004f,1.35f,-.069f,Shade(Teal,.45f));
            screen.Quad(.392f,-.58f,.002f,1.10f,-.069f,Shade(Teal,.4f));
            paint.Add("Screen status and glass edge",screen);
            string[] icons={"0011000/0111100/1100010/1100010/1100010/0111100/0011000",
                "0011100/0010100/1111111/1000001/1111111/1001001/1111111",
                "0011100/0010100/0111110/0100010/0101010/0100010/0111110",
                "1111111/1000001/1111111/1010101/1111111/1001001/1111111",
                "1000000/1000010/1000101/1011001/1100001/1000001/1111111",
                "0001000/0011100/0101010/1001001/0011100/0001000/0011100",
                "0001110/0001010/0001010/0001010/0111010/1111110/0110010",
                "1111111/1001001/1001101/1001111/1001101/1001001/1111111",
                "0011100/0110110/1100011/1011101/1100011/0110110/0011100"};
            Color[] accents={Teal,Brass,new(.45f,.56f,.37f),new(.46f,.54f,.44f),new(.58f,.47f,.29f),new(.32f,.50f,.54f),new(.45f,.53f,.43f),new(.56f,.41f,.31f),new(.39f,.48f,.48f)};
            for(int i=0;i<9;i++)
            {
                float cx=-.25f+(i%3)*.25f,cy=.345f-(i/3)*.265f;
                var app=new Shape();app.Polygon(Rounded(cx,cy-.006f,.179f,.179f,.027f),-.071f,Shade(Ink,.8f));
                app.Polygon(Rounded(cx,cy,.173f,.173f,.026f),-.073f,accents[i]);
                Pixels(app,icons[i],cx-.057f,cy+.056f,.016f,-.076f,Cream);
                for(int p=0;p<3;p++)app.Quad(cx-.024f+p*.020f,cy-.113f,.011f,.003f,-.072f,Shade(Teal,.9f));
                if(i==2)app.Disc(cx+.075f,cy+.075f,.014f,-.077f,new Color(.92f,.65f,.29f));
                paint.Add($"Screen app {i%3},{i/3}",app);
            }
            Plane(paint,"Quick access dock",0,-.575f,.66f,.16f,.041f,-.071f,Shade(Ink,1.3f));
            var dock=new Shape();
            Pixels(dock,"11000/11000/01000/01111/00111",-.248f,-.535f,.013f,-.075f,Cream);
            Pixels(dock,"11111/10001/10101/10001/11111",-.032f,-.535f,.013f,-.075f,Teal);
            Pixels(dock,"01110/10101/11111/10101/01110",.187f,-.535f,.013f,-.075f,Cream);
            for(int i=0;i<3;i++)dock.Disc(-.018f+i*.018f,-.724f,.004f,-.072f,i==0?Cream:Shade(Teal,.55f));
            paint.Add("Dock glyphs and page indicator",dock);
            Plane(paint,"Gesture strip",0,-.845f,.205f,.018f,.009f,-.067f,Shade(Cream,.7f));

            Plane(paint,"Front earpiece",.030f,.850f,.18f,.020f,.010f,-.066f,Ink);
            var speaker=new Shape();for(int i=0;i<6;i++)speaker.Quad(-.035f+i*.023f,.845f,.006f,.010f,-.069f,Shade(Brass,.45f));paint.Add("Earpiece grille",speaker);
            Lens(paint,"Front camera",-.180f,.850f,.018f,-.067f,false);
            Plane(paint,"Rear service backplate",0,0,.903f,1.796f,.095f,.067f,new Color(.14f,.215f,.215f),true);
            RaisedPlate(paint,"Rear camera island",-.245f,.61f,.346f,.433f,.056f,.068f,.073f,Ink);
            Lens(paint,"Rear wide camera",-.275f,.699f,.071f,.077f,true);
            Lens(paint,"Rear auxiliary camera",-.275f,.527f,.059f,.077f,true);
            var rear=new Shape();rear.Disc(-.155f,.602f,.025f,.077f,Shade(Cream,.85f),true);rear.Disc(-.155f,.51f,.012f,.077f,Shade(Teal,.6f),true);
            rear.Quad(-.105f,-.641f,.21f,.008f,.069f,Shade(Brass,.45f),true);
            rear.Quad(-.075f,-.667f,.15f,.004f,.069f,Shade(Brass,.35f),true);
            foreach(float x in new[]{-.367f,.367f})foreach(float y in new[]{-.806f,.806f})
            {rear.Disc(x,y,.012f,.070f,Shade(Brass,.65f),true);rear.Quad(x-.006f,y-.001f,.012f,.002f,.072f,Ink,true);}
            paint.Add("Rear flash, maker mark and screws",rear);
            Box(paint,"Power button",.495f,.26f,0,.018f,.21f,.041f,Brass);
            Box(paint,"Volume up",-.495f,.35f,0,.018f,.135f,.041f,Shade(Brass,.9f));
            Box(paint,"Volume down",-.495f,.15f,0,.018f,.135f,.041f,Shade(Brass,.9f));
            Box(paint,"Charging socket",0,-.943f,.006f,.135f,.008f,.035f,Ink);
            for(int i=0;i<5;i++)Box(paint,$"Bottom speaker slot {i}",.173f+i*.031f,-.943f,.005f,.010f,.008f,.033f,Ink);
            Box(paint,"Bottom microphone",-.233f,-.943f,.005f,.025f,.008f,.025f,Ink);
            foreach(float x in new[]{-.492f,.492f})foreach(float y in new[]{-.665f,.661f})Box(paint,"Antenna separator",x,y,.003f,.009f,.015f,.060f,Shade(Cream,.42f));
            var wear=new Shape();
            wear.Quad(-.425f,-.866f,.040f,.003f,-.064f,Shade(Cream,.5f));
            wear.Quad(-.434f,-.853f,.003f,.035f,-.064f,Shade(Cream,.36f));
            wear.Quad(.405f,.818f,.002f,.032f,-.064f,Shade(Cream,.35f));
            paint.Add("Localized frame wear",wear);
            paint.Complete();
            return root;
        }
        catch
        {
            Object.Destroy(root);
            ReleaseFrom(meshes,meshStart);ReleaseFrom(materials,materialStart);ReleaseFrom(textures,textureStart);
            throw;
        }
    }

    private static void Chassis(Paint paint)
    {
        var shape=new Shape();
        var rings=new[]{Rounded(0,0,.937f,1.837f,.102f),Rounded(0,0,.982f,1.882f,.120f),Rounded(0,0,.982f,1.882f,.120f),Rounded(0,0,.931f,1.831f,.101f)};
        float[] z={.064f,.041f,-.035f,-.062f};
        shape.Polygon(rings[0],z[0],new Color(.15f,.22f,.215f),true);
        for(int band=0;band<3;band++)for(int i=0;i<rings[band].Length;i++)
        {
            int next=(i+1)%rings[band].Length;
            var a=rings[band][i];var b=rings[band+1][i];var c=rings[band+1][next];var d=rings[band][next];
            float highlight=.76f+.13f*(a.y/.94f)-.10f*(a.x/.49f);
            var color=Shade(band==1?Brass:new Color(.75f,.70f,.52f),highlight+(band==2?.18f:0));
            shape.Face(new(a.x,a.y,z[band]),new(b.x,b.y,z[band+1]),new(c.x,c.y,z[band+1]),new(d.x,d.y,z[band]),color);
        }
        shape.Polygon(rings[3],z[3],new Color(.095f,.135f,.135f));
        paint.Add("Rounded metal chassis with shaded bevels",shape);
    }
    private static Vector2[] Rounded(float cx,float cy,float width,float height,float radius)
    {
        var ring=new List<Vector2>();radius=Math.Min(radius,Math.Min(width,height)/2);
        for(int corner=0;corner<4;corner++)
        {
            float x=cx+(corner<2?1:-1)*(width/2-radius),y=cy+(corner==1||corner==2?1:-1)*(height/2-radius);
            for(int step=0;step<=5;step++)
            {float angle=(-90+corner*90+step*18)*MathF.PI/180;ring.Add(new Vector2(x+MathF.Cos(angle)*radius,y+MathF.Sin(angle)*radius));}
        }
        return ring.ToArray();
    }
    private static void Plane(Paint p,string name,float x,float y,float w,float h,float r,float z,Color c,bool rear=false)
    {var shape=new Shape();shape.Polygon(Rounded(x,y,w,h,r),z,c,rear);p.Add(name,shape);}
    private static void RaisedPlate(Paint p,string name,float x,float y,float w,float h,float r,float low,float high,Color color)
    {
        var s=new Shape();var ring=Rounded(x,y,w,h,r);s.Polygon(ring,high,color,true);
        s.Band(ring,low,high,Shade(color,.85f));p.Add(name,s);
    }
    private static void Lens(Paint p,string name,float x,float y,float radius,float z,bool rear)
    {
        var s=new Shape();float direction=rear?1:-1;
        var ring=Shape.Circle(x,y,radius);s.Band(ring,Math.Min(z,z+direction*.003f),Math.Max(z,z+direction*.003f),Shade(Brass,.65f));
        s.Disc(x,y,radius,z+direction*.003f,Brass,rear);s.Disc(x,y,radius*.82f,z+direction*.004f,Ink,rear);
        s.Disc(x,y,radius*.58f,z+direction*.006f,new Color(.07f,.22f,.24f),rear);
        s.Disc(x+radius*.18f,y+radius*.17f,radius*.19f,z+direction*.009f,Shade(Teal,1.18f),rear);
        p.Add(name,s);
    }
    private static void Box(Paint p,string name,float x,float y,float z,float w,float h,float d,Color color)
    {
        float l=x-w/2,r=x+w/2,b=y-h/2,t=y+h/2,f=z-d/2,k=z+d/2;var s=new Shape();
        s.Face(new(l,b,f),new(l,t,f),new(r,t,f),new(r,b,f),Shade(color,1.12f));
        s.Face(new(l,b,k),new(r,b,k),new(r,t,k),new(l,t,k),Shade(color,.85f));
        s.Face(new(l,b,f),new(r,b,f),new(r,b,k),new(l,b,k),Shade(color,.65f));
        s.Face(new(l,t,f),new(l,t,k),new(r,t,k),new(r,t,f),Shade(color,1.2f));
        s.Face(new(l,b,f),new(l,b,k),new(l,t,k),new(l,t,f),Shade(color,.95f));
        s.Face(new(r,b,f),new(r,t,f),new(r,t,k),new(r,b,k),Shade(color,.7f));p.Add(name,s);
    }
    private static Color Shade(Color c,float value)=>new(Math.Clamp(c.r*value,0,1),Math.Clamp(c.g*value,0,1),Math.Clamp(c.b*value,0,1),c.a);
    private static void Pixels(Shape s,string bitmap,float x,float y,float size,float z,Color color)
    {
        string[] rows=bitmap.Split('/');for(int row=0;row<rows.Length;row++)for(int col=0;col<rows[row].Length;col++)
            if(rows[row][col]=='1')s.Quad(x+col*size,y-row*size,size*.88f,size*.88f,z,color);
    }
    private static void Glyph(Shape s,string text,float x,float y,float size,float z,Color color)
    {
        var font=new Dictionary<char,string>{{'0',"111/101/101/101/111"},{'9',"111/101/111/001/111"},{'4',"101/101/111/001/001"},{'1',"010/110/010/010/111"},{':',"0/1/0/1/0"},{'O',"111/101/101/101/111"},{'R',"110/101/110/101/101"},{'B',"110/101/110/101/110"},{'I',"111/010/010/010/111"},{'T',"111/010/010/010/010"}};
        foreach(char letter in text){if(font.TryGetValue(letter,out var glyph))Pixels(s,glyph,x,y,size,z,color);x+=(letter==':'?2:4)*size;}
    }

    private sealed class Shape
    {
        public readonly List<Vector3> Vertices=new();public readonly List<int> Triangles=new();public readonly List<Color> Colors=new();
        public void Face(Vector3 a,Vector3 b,Vector3 c,Vector3 d,Color color)
        {int n=Vertices.Count;Vertices.AddRange(new[]{a,b,c,d});Triangles.AddRange(new[]{n,n+1,n+2,n,n+2,n+3});Colors.AddRange(new[]{color,color,color,color});}
        public void Quad(float x,float y,float w,float h,float z,Color c,bool rear=false)
        {if(rear)Face(new(x,y,z),new(x+w,y,z),new(x+w,y+h,z),new(x,y+h,z),c);else Face(new(x,y,z),new(x,y+h,z),new(x+w,y+h,z),new(x+w,y,z),c);}
        public void Polygon(Vector2[] ring,float z,Color color,bool rear=false)
        {
            var center=new Vector3(ring.Average(v=>v.x),ring.Average(v=>v.y),z);
            for(int i=0;i<ring.Length;i++)
            {
                var a=ring[i];var b=ring[(i+1)%ring.Length];int n=Vertices.Count;
                Vertices.Add(center);Vertices.Add(new Vector3(rear?a.x:b.x,rear?a.y:b.y,z));Vertices.Add(new Vector3(rear?b.x:a.x,rear?b.y:a.y,z));
                Triangles.AddRange(new[]{n,n+1,n+2});Colors.AddRange(new[]{color,color,color});
            }
        }
        public void Disc(float x,float y,float radius,float z,Color color,bool rear=false)
        {Polygon(Circle(x,y,radius),z,color,rear);}
        public static Vector2[] Circle(float x,float y,float radius)
        {var ring=new Vector2[16];for(int i=0;i<ring.Length;i++){float a=i*MathF.PI*2/ring.Length;ring[i]=new Vector2(x+MathF.Cos(a)*radius,y+MathF.Sin(a)*radius);}return ring;}
        public void Band(Vector2[] ring,float low,float high,Color color)
        {for(int i=0;i<ring.Length;i++){var a=ring[i];var b=ring[(i+1)%ring.Length];Face(new(a.x,a.y,low),new(b.x,b.y,low),new(b.x,b.y,high),new(a.x,a.y,high),color);}}
    }
    private sealed class Paint
    {
        private readonly WorldPhone owner;private readonly GameObject root;private readonly Material material;
        private readonly Texture2D? palette;private readonly Dictionary<uint,int> swatches=new();
        public Paint(WorldPhone owner,GameObject root)
        {
            this.owner=owner;this.root=root;
            var shader=Shader.Find("Unlit/Texture");
            if(shader!=null)
            {
                palette=new Texture2D(256,1,TextureFormat.RGBA32,false);palette.name="SpacePhone industrial palette";
                palette.filterMode=FilterMode.Point;palette.wrapMode=TextureWrapMode.Clamp;owner.textures.Add(palette);
            }
            else shader=Shader.Find("Hidden/Internal-Colored");
            if(shader==null)shader=Shader.Find("Sprites/Default");
            if(shader==null)throw new InvalidOperationException("Phone material shader unavailable");
            material=new Material(shader);material.name="SpacePhone handset material";
            if(material.HasProperty("_Color"))material.color=Color.white;
            material.mainTexture=palette??Texture2D.whiteTexture;
            // Native shop art is sorted in the transparent queue. Keep that order while retaining opaque handset depth.
            material.renderQueue=3000;
            if(shader.name=="Hidden/Internal-Colored")
            {material.SetInt("_SrcBlend",1);material.SetInt("_DstBlend",0);material.SetInt("_Cull",2);material.SetInt("_ZWrite",1);material.SetInt("_ZTest",4);}
            owner.materials.Add(material);
        }
        public void Add(string name,Shape shape)
        {
            var uv=new Vector2[shape.Colors.Count];
            if(palette!=null)for(int i=0;i<uv.Length;i++)
            {
                Color32 c=shape.Colors[i];uint key=(uint)(c.r|c.g<<8|c.b<<16|c.a<<24);
                if(!swatches.TryGetValue(key,out int index))
                {index=swatches.Count;if(index>=256)throw new InvalidOperationException("Phone palette overflow");swatches.Add(key,index);palette.SetPixel(index,0,c);}
                uv[i]=new Vector2((index+.5f)/256,.5f);
            }
            var go=new GameObject(name);go.transform.SetParent(root.transform,false);
            var mesh=new Mesh();mesh.name="SpacePhone "+name;mesh.vertices=shape.Vertices.ToArray();mesh.triangles=shape.Triangles.ToArray();
            mesh.colors32=shape.Colors.Select(c=>(Color32)c).ToArray();mesh.uv=uv;mesh.RecalculateBounds();mesh.RecalculateNormals();owner.meshes.Add(mesh);
            go.AddComponent(Il2CppType.Of<MeshFilter>()).Cast<MeshFilter>().sharedMesh=mesh;
            var renderer=go.AddComponent(Il2CppType.Of<MeshRenderer>()).Cast<MeshRenderer>();renderer.sharedMaterial=material;
            renderer.shadowCastingMode=UnityEngine.Rendering.ShadowCastingMode.Off;renderer.receiveShadows=false;
        }
        public void Complete()=>palette?.Apply(false,false);
    }
    private static void ReleaseFrom<T>(List<T> list,int start) where T:Object
    {for(int i=start;i<list.Count;i++)Object.Destroy(list[i]);if(list.Count>start)list.RemoveRange(start,list.Count-start);}
    public void Destroy()
    {
        foreach(var replacement in replacements){if(replacement.Original!=null)replacement.Original.enabled=true;if(replacement.Model!=null)Object.Destroy(replacement.Model);}
        replacements.Clear();ReleaseFrom(meshes,0);ReleaseFrom(materials,0);ReleaseFrom(textures,0);
    }
}
