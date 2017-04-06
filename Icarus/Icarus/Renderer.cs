﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;
using Microsoft.DirectX;
using Microsoft.DirectX.Direct3D;
using System.Diagnostics;

namespace Icarus
{
    public sealed class Renderer
    {
        private static readonly Renderer sInstance = new Renderer();

        private bool mInitialized = false, mCameraNeedsRebuild = false, /*mRenderGrid = true,*/ mPanelResizing = false;
        private Device mDevice;
        private Control mPanel;
        private Timer mResizeTimeout = new Timer();

        private Vector3 mCameraPos = new Vector3(0, 2, 4);
        private float mAngleX = 0, mAngleY = 0;

        public static Renderer Instance {
            get { return sInstance; }
        }

        public float AngleY {
            get {
                return mAngleY;
            }
            set {
                mAngleY = value;
                mCameraNeedsRebuild = true;
            }
        }

        public float AngleX {
            get {
                return mAngleX;
            }
            set {
                mAngleX = value;
                mCameraNeedsRebuild = true;
            }
        }

        public Matrix View {
            get {
                return mDevice.Transform.View;
            }
        }

        public Vector3 CameraPosition {
            get {
                return mCameraPos;
            }
            set {
                mCameraPos = value;
                mCameraNeedsRebuild = true;
            }
        }

        public Vector3 Forward {
            get {
                // TODO: cache the reuslt into a variable and only recalcualte it when necessary.
                Vector3 forward = new Vector3(0, 0, 1);
                return RotateInto(forward, View);
            }
        }

        public Vector3 Up {
            get {
                // TODO: cache the reuslt into a variable and only recalcualte it when necessary.
                Vector3 up = new Vector3(0, 1, 0);
                return RotateInto(up, View);
            }
        }

        public Vector3 Right {
            get {
                // TODO: cache the reuslt into a variable and only recalcualte it when necessary.
                Vector3 right = new Vector3(1, 0, 0);
                return RotateInto(right, View);
            }
        }

        public Device Device {
            get { return mDevice; }
        }

        private Renderer() { }


        public void AttachToControl(Control _panel)
        {
            mPanel = _panel;
            PresentParameters p = new PresentParameters();
            p.Windowed = true;
            p.SwapEffect = SwapEffect.Copy;
            p.EnableAutoDepthStencil = true;
            p.AutoDepthStencilFormat = DepthFormat.D16;
            mDevice = new Device(0, DeviceType.Hardware, mPanel, CreateFlags.HardwareVertexProcessing, p);
            mDevice.RenderState.CullMode = Cull.None;
            mDevice.RenderState.FillMode = FillMode.Solid;
            mDevice.RenderState.ZBufferWriteEnable = true;
            mDevice.RenderState.ZBufferEnable = true;


            RebuildProjectionMatrix();
            RebuildViewMatrix();

            mRenderSet = new List<BaseObject>();

            mDevice.DeviceReset += HandleDeviceReset;
            _panel.Resize += ResizeBegin;
            mResizeTimeout.Tick += ResizeEnd;


            mInitialized = true;
        }

        public void RebuildProjectionMatrix()
        {
            mDevice.Transform.Projection = Matrix.PerspectiveFovRH((float)Math.PI / 4.0f, (float)mPanel.Width / (float)mPanel.Height, 0.1f, 1000);
        }

        public void RebuildViewMatrix()
        {
            Matrix rotate = Matrix.RotationYawPitchRoll(mAngleY, mAngleX, 0);
            Vector4 transform = Vector3.Transform(new Vector3(0, 0, 3), rotate);
            Vector3 look = mCameraPos + new Vector3(transform.X, transform.Y, transform.Z);
            mDevice.Transform.View = Matrix.LookAtRH(mCameraPos, look, new Vector3(0, 1, 0));
            mDevice.RenderState.Lighting = false;
            mDevice.RenderState.ZBufferEnable = true;
            mDevice.RenderState.CullMode = Cull.Clockwise;
            mCameraNeedsRebuild = false;
        }

        public void Render()
        {
            if (!mInitialized) {
                Debug.Print("Attempting to render the scene before setting up the renderer!");
                return;
            }
            if (mCameraNeedsRebuild) {
                // Rebuild the view matrix based on the camera position and rotation.
                RebuildViewMatrix();
            }

            mDevice.BeginScene();

            List<BaseObject>.Enumerator objectIterator = mRenderSet.GetEnumerator();
            mDevice.Clear(ClearFlags.ZBuffer, 0, 1.0f, 0);
            mDevice.Clear(ClearFlags.Target, System.Drawing.Color.Black, 1.0f, 0);
            mDevice.RenderState.ZBufferEnable = true;
            while (objectIterator.MoveNext()) {
                // Find which components are shapes
                List<Component>.Enumerator componentIterator = objectIterator.Current.GetComponents().GetEnumerator();
                while (componentIterator.MoveNext()) {
                    // Render the shape.
                    if (!(componentIterator.Current is IRenderable) || !((IRenderable)componentIterator.Current).Shape.Valid) {
                        continue;
                    }

                    if (((IRenderable)componentIterator.Current).Shape.Type == RenderShape.ShapeType.Textured) {
                        mDevice.VertexFormat = CustomVertex.PositionNormalTextured.Format;
                        TexturedShape ts = ((IRenderable)componentIterator.Current).Shape as TexturedShape;
                        if (ts.Textures[(int)TexturedShape.TextureType.Diffuse] != null) {
                            mDevice.SetTexture(0, ts.Textures[(int)TexturedShape.TextureType.Diffuse]);
                        }
                        else {
                            // TODO: Default texture.
                            mDevice.SetTexture(0, null);
                        }
                    }
                    else {
                        mDevice.VertexFormat = CustomVertex.PositionNormalColored.Format;
                        mDevice.SetTexture(0, null);
                        mDevice.RenderState.AlphaBlendEnable = false;
                        mDevice.RenderState.CullMode = Cull.None;
                    }
                    mDevice.Indices = ((IRenderable)componentIterator.Current).Shape.IndexBuffer;
                    mDevice.SetStreamSource(0, ((IRenderable)componentIterator.Current).Shape.VertexBuffer, 0);
                    mDevice.RenderState.FillMode = ((IRenderable)componentIterator.Current).Shape.FillMode;
                    mDevice.Transform.World = ((IRenderable)componentIterator.Current).Shape.World * objectIterator.Current.GetMatrix();
                    mDevice.DrawIndexedPrimitives(PrimitiveType.TriangleList, 0, 0, ((IRenderable)componentIterator.Current).Shape.Indices.Length, 0, ((IRenderable)componentIterator.Current).Shape.Indices.Length / 3);
                }
            }
            Gizmo.Instance.Render();

            mDevice.EndScene();
            mDevice.Present();
        }

        public Texture LoadTexture(string file)
        {
            return TextureLoader.FromFile(mDevice, file);
        }

        //public void AddObject(BaseObject _m)
        //{
        //    mRenderSet.Add(_m);
        //}
        //
        //public bool RemoveObject(BaseObject _m)
        //{
        //    return mRenderSet.Remove(_m);
        //}

        public Vector3 RotateInto(Vector3 _p, Matrix _m)
        {
            // These matrices are column major, so be sure to multiply correctly!
            Vector3 transformed = new Vector3(0, 0, 0);
            transformed.X = _p.X * _m.M11 + _p.Y * _m.M12 + _p.Z * _m.M13;
            transformed.Y = _p.X * _m.M21 + _p.Y * _m.M22 + _p.Z * _m.M23;
            transformed.Z = _p.X * _m.M31 + _p.Y * _m.M32 + _p.Z * _m.M33;
            return transformed;
        }

        public Vector3 RotateInto(Vector4 _p, Matrix _m)
        {
            // These matrices are column major, so be sure to multiply correctly!
            Vector3 transformed = new Vector3(0, 0, 0);
            transformed.X = _p.X * _m.M11 + _p.Y * _m.M12 + _p.Z * _m.M13;
            transformed.Y = _p.X * _m.M21 + _p.Y * _m.M22 + _p.Z * _m.M23;
            transformed.Z = _p.X * _m.M31 + _p.Y * _m.M32 + _p.Z * _m.M33;
            return transformed;
        }
        #region Event Handlers

        private void HandleDeviceReset(object sender, EventArgs e)
        {
            if (!mPanelResizing) {
                mDevice.RenderState.FillMode = FillMode.Solid;
                mDevice.RenderState.CullMode = Cull.Clockwise;
                mDevice.RenderState.ZBufferEnable = true;
                RebuildViewMatrix();
                RebuildProjectionMatrix();
                for (int i = 0; i < mRenderSet.Count; ++i) {
                    List<Component> comps = mRenderSet[i].GetComponents();
                    for (int j = 0; j < comps.Count; ++j) {
                        if (comps[j] is MeshComponent) {
                            ((MeshComponent)comps[j]).Shape.FillBuffers();
                        }
                    }
                }
                Gizmo.Instance.Reset();
            }
        }

        private void ResizeBegin(object sender, EventArgs e)
        {
            mResizeTimeout.Stop(); // Prevent a previous timer from continuing.
            mResizeTimeout.Interval = 100; // The timeout ro rebuild all the buffers is 1/10th of a second.
            mPanelResizing = true;
            mResizeTimeout.Start();
        }

        private void ResizeEnd(object sender, EventArgs e)
        {
            mResizeTimeout.Stop(); // So that the timer doesn't keep ticking
            mPanelResizing = false;
            HandleDeviceReset(null, null);
        }

        #endregion
    }
}
