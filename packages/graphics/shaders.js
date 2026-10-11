export const WGSL=`
struct Uniforms {
 matrix: mat4x4<f32>,
 eye: vec4<f32>,
 color: vec4<f32>,
 material: vec4<f32>,
 viewport: vec4<f32>,
 clip: vec4<f32>,
};
@group(0) @binding(0) var<uniform> u: Uniforms;
struct VertexOut {@builtin(position) position:vec4<f32>, @location(0) world:vec3<f32>, @location(1) normal:vec3<f32>};
@vertex fn surfaceVertex(@location(0) position:vec3<f32>,@location(1) normal:vec3<f32>)->VertexOut {
 var out:VertexOut;out.position=u.matrix*vec4<f32>(position,1.0);out.world=position;out.normal=normal;return out;
}
fn fresnel(cosine:f32,f0:vec3<f32>)->vec3<f32>{return f0+(vec3<f32>(1.0)-f0)*pow(1.0-cosine,5.0);}
fn lightContribution(n:vec3<f32>,v:vec3<f32>,l:vec3<f32>,base:vec3<f32>,radiance:vec3<f32>)->vec3<f32>{
 let rough=clamp(u.material.x,0.05,1.0);let metal=clamp(u.material.y,0.0,1.0);let h=normalize(v+l);
 let nl=max(dot(n,l),0.0);let nv=max(dot(n,v),0.0001);let nh=max(dot(n,h),0.0);let hv=max(dot(h,v),0.0);
 let a=rough*rough;let a2=a*a;let denom=nh*nh*(a2-1.0)+1.0;let distribution=a2/max(3.14159265*denom*denom,0.0001);
 let k=(rough+1.0)*(rough+1.0)/8.0;let g1=nv/(nv*(1.0-k)+k);let g2=nl/(nl*(1.0-k)+k);
 let f=fresnel(hv,mix(vec3<f32>(0.04),base,metal));let spec=f*distribution*g1*g2/max(4.0*nl*nv,0.0001);
 let diffuse=(vec3<f32>(1.0)-f)*(1.0-metal)*base/3.14159265;return(diffuse+spec)*radiance*nl;
}
@fragment fn surfaceFragment(input:VertexOut,@builtin(front_facing) front:bool)->@location(0) vec4<f32>{
 if(u.material.w>0.5 && dot(u.clip.xyz,input.world)+u.clip.w<0.0){discard;}
 var normal=normalize(input.normal);if(!front){normal=-normal;}
 let view=normalize(u.eye.xyz-input.world);let base=pow(u.color.rgb,vec3<f32>(2.2));
 var color=base*(0.22+0.12*max(normal.z,0.0));
 color+=lightContribution(normal,view,normalize(vec3<f32>(-0.3,-0.5,0.85)),base,vec3<f32>(2.5,2.4,2.25));
 color+=lightContribution(normal,view,normalize(vec3<f32>(0.65,0.4,0.5)),base,vec3<f32>(1.1,1.2,1.4));
 color+=lightContribution(normal,view,normalize(vec3<f32>(-0.2,0.7,-0.1)),base,vec3<f32>(0.4));
 color=color/(vec3<f32>(1.0)+color);color=pow(color,vec3<f32>(1.0/2.2));return vec4<f32>(color,u.color.a);
}
struct LineOut {@builtin(position) position:vec4<f32>, @location(0) side:f32, @location(1) world:vec3<f32>};
@vertex fn lineVertex(@builtin(vertex_index) vertex:u32,@location(0) a:vec3<f32>,@location(1) b:vec3<f32>)->LineOut {
 let endpoint=array<f32,6>(0.0,1.0,1.0,0.0,1.0,0.0);let sides=array<f32,6>(-1.0,-1.0,1.0,-1.0,1.0,1.0);
 let pa=u.matrix*vec4<f32>(a,1.0);let pb=u.matrix*vec4<f32>(b,1.0);let na=pa.xy/max(abs(pa.w),0.000001);let nb=pb.xy/max(abs(pb.w),0.000001);
 let screenDelta=(nb-na)*u.viewport.xy;let magnitude=max(length(screenDelta),0.000001);let perpendicular=vec2<f32>(-screenDelta.y,screenDelta.x)/magnitude;
 var position=mix(pa,pb,endpoint[vertex]);position=vec4<f32>(position.xy+perpendicular/u.viewport.xy*u.viewport.z*sides[vertex]*position.w,position.z-0.000015*position.w,position.w);
 var out:LineOut;out.position=position;out.side=sides[vertex];out.world=mix(a,b,endpoint[vertex]);return out;
}
@fragment fn lineFragment(input:LineOut)->@location(0) vec4<f32>{if(u.material.w>0.5&&dot(u.clip.xyz,input.world)+u.clip.w<0.0){discard;}let coverage=1.0-smoothstep(0.45,1.0,abs(input.side));return vec4<f32>(u.color.rgb,u.color.a*coverage);}
`;
export const GLSL={
 vertex:`#version 300 es
precision highp float;layout(location=0)in vec3 position;layout(location=1)in vec3 normal;uniform mat4 matrix;out vec3 world;out vec3 vertexNormal;
void main(){gl_Position=matrix*vec4(position,1.0);world=position;vertexNormal=normal;}`,
 fragment:`#version 300 es
precision highp float;in vec3 world;in vec3 vertexNormal;uniform vec3 eye;uniform vec4 color;uniform vec4 material;uniform vec4 clipPlane;out vec4 outputColor;
vec3 fresnel(float c,vec3 f0){return f0+(1.0-f0)*pow(1.0-c,5.0);}
vec3 light(vec3 n,vec3 v,vec3 l,vec3 base,vec3 radiance){float rough=clamp(material.x,.05,1.0),metal=clamp(material.y,0.0,1.0);vec3 h=normalize(v+l);float nl=max(dot(n,l),0.0),nv=max(dot(n,v),.0001),nh=max(dot(n,h),0.0),hv=max(dot(h,v),0.0),a=rough*rough,a2=a*a,d=nh*nh*(a2-1.0)+1.0,distribution=a2/max(3.14159265*d*d,.0001),k=(rough+1.0)*(rough+1.0)/8.0,g1=nv/(nv*(1.0-k)+k),g2=nl/(nl*(1.0-k)+k);vec3 f=fresnel(hv,mix(vec3(.04),base,metal));return((1.0-f)*(1.0-metal)*base/3.14159265+f*distribution*g1*g2/max(4.0*nl*nv,.0001))*radiance*nl;}
void main(){if(material.w>.5&&dot(clipPlane.xyz,world)+clipPlane.w<0.0)discard;vec3 n=normalize(vertexNormal);if(!gl_FrontFacing)n=-n;vec3 v=normalize(eye-world),base=pow(color.rgb,vec3(2.2));vec3 c=base*(.22+.12*max(n.z,0.0));c+=light(n,v,normalize(vec3(-.3,-.5,.85)),base,vec3(2.5,2.4,2.25));c+=light(n,v,normalize(vec3(.65,.4,.5)),base,vec3(1.1,1.2,1.4));c+=light(n,v,normalize(vec3(-.2,.7,-.1)),base,vec3(.4));c=c/(1.0+c);outputColor=vec4(pow(c,vec3(1.0/2.2)),color.a);}`,
 lineVertex:`#version 300 es
precision highp float;layout(location=0)in vec3 startPoint;layout(location=1)in vec3 endPoint;uniform mat4 matrix;uniform vec3 viewport;out float side;out vec3 world;
void main(){float endpoints[6]=float[6](0.,1.,1.,0.,1.,0.);float sides[6]=float[6](-1.,-1.,1.,-1.,1.,1.);vec4 a=matrix*vec4(startPoint,1.),b=matrix*vec4(endPoint,1.);vec2 delta=(b.xy/max(abs(b.w),.000001)-a.xy/max(abs(a.w),.000001))*viewport.xy;vec2 perp=vec2(-delta.y,delta.x)/max(length(delta),.000001);vec4 p=mix(a,b,endpoints[gl_VertexID]);p.xy+=perp/viewport.xy*viewport.z*sides[gl_VertexID]*p.w;p.z-=.000015*p.w;gl_Position=p;side=sides[gl_VertexID];world=mix(startPoint,endPoint,endpoints[gl_VertexID]);}`,
 lineFragment:`#version 300 es
precision highp float;in float side;in vec3 world;uniform vec4 color;uniform vec4 material;uniform vec4 clipPlane;out vec4 outputColor;void main(){if(material.w>.5&&dot(clipPlane.xyz,world)+clipPlane.w<0.)discard;outputColor=vec4(color.rgb,color.a*(1.-smoothstep(.45,1.,abs(side))));}`
};
