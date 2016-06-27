﻿using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;

namespace Barotrauma.Lights
{
    class CachedShadow : IDisposable
    {
        public VertexBuffer ShadowBuffer;

        public Vector2 LightPos;

        public int ShadowVertexCount, PenumbraVertexCount;

        public CachedShadow(VertexPositionColor[] shadowVertices, Vector2 lightPos, int shadowVertexCount, int penumbraVertexCount)
        {
            //var ShadowVertices = new VertexPositionColor [shadowVertices.Count()];
            //shadowVertices.CopyTo(ShadowVertices, 0);

            ShadowBuffer = new VertexBuffer(GameMain.CurrGraphicsDevice, VertexPositionColor.VertexDeclaration, 6*2, BufferUsage.None);
            ShadowBuffer.SetData(shadowVertices, 0, shadowVertices.Length);

            ShadowVertexCount = shadowVertexCount;
            PenumbraVertexCount = penumbraVertexCount;

            LightPos = lightPos;
        }

        public void Dispose()
        {
            Dispose(true);

            GC.SuppressFinalize(this);
        }

        protected virtual void Dispose(bool disposing)
        {
            ShadowBuffer.Dispose();
        }
    }

    class ConvexHull
    {
        public static List<ConvexHull> list = new List<ConvexHull>();
        static BasicEffect shadowEffect;
        static BasicEffect penumbraEffect;

        private Dictionary<LightSource, CachedShadow> cachedShadows;
                
        private Vector2[] vertices;
        private Vector2[] losVertices;
        private int primitiveCount;

        private bool[] backFacing;
        private bool[] ignoreEdge;

        private VertexPositionColor[] shadowVertices;
        private VertexPositionTexture[] penumbraVertices;
        
        int shadowVertexCount;

        private Entity parentEntity;

        private Rectangle boundingBox;

        public Entity ParentEntity
        {
            get { return parentEntity; }

        }

        public bool Enabled
        {
            get;
            set;
        }

        public Rectangle BoundingBox
        {
            get { return boundingBox; }
        }
                
        public ConvexHull(Vector2[] points, Color color, Entity parent)
        {
            if (shadowEffect == null)
            {
                shadowEffect = new BasicEffect(GameMain.CurrGraphicsDevice);
                shadowEffect.VertexColorEnabled = true;
            }
            if (penumbraEffect == null)
            {
                penumbraEffect = new BasicEffect(GameMain.CurrGraphicsDevice);
                penumbraEffect.TextureEnabled = true;
                //shadowEffect.VertexColorEnabled = true;
                penumbraEffect.LightingEnabled = false;
                penumbraEffect.Texture = TextureLoader.FromFile("Content/Lights/penumbra.png");
            }

            parentEntity = parent;

            cachedShadows = new Dictionary<LightSource, CachedShadow>();
            
            shadowVertices = new VertexPositionColor[6 * 2];
            penumbraVertices = new VertexPositionTexture[6];
            
            //vertices = points;
            primitiveCount = points.Length;
            SetVertices(points);
            //CalculateDimensions();
            
            backFacing = new bool[primitiveCount];
            ignoreEdge = new bool[primitiveCount];
                        
            Enabled = true;

            foreach (ConvexHull ch in list)
            {
                UpdateIgnoredEdges(ch);
                ch.UpdateIgnoredEdges(this);
            }


            list.Add(this);

        }

        private void UpdateIgnoredEdges(ConvexHull ch)
        {
            for (int i = 0; i < vertices.Length; i++)
            {
                if (vertices[i].X >= ch.boundingBox.X && vertices[i].X <= ch.boundingBox.Right && 
                    vertices[i].Y >= ch.boundingBox.Y && vertices[i].Y <= ch.boundingBox.Bottom)
                {
                    Vector2 p = vertices[(i + 1) % vertices.Length];

                    if (p.X >= ch.boundingBox.X && p.X <= ch.boundingBox.Right && 
                        p.Y >= ch.boundingBox.Y && p.Y <= ch.boundingBox.Bottom)
                    {
                        ignoreEdge[i] = true;
                    }
                }                    
            }            
        }
        
        private void CalculateDimensions()
        {
            float? minX = null, minY = null, maxX = null, maxY = null;

            for (int i = 0; i < vertices.Length; i++)
            {
                if (minX == null || vertices[i].X < minX) minX = vertices[i].X;
                if (minY == null || vertices[i].Y < minY) minY = vertices[i].Y;

                if (maxX == null || vertices[i].X > maxX) maxX = vertices[i].X;
                if (maxY == null || vertices[i].Y > minY) maxY = vertices[i].Y;
            }

            boundingBox = new Rectangle((int)minX, (int)minY, (int)(maxX - minX), (int)(maxY - minY));
        }
                
        public void Move(Vector2 amount)
        {
            ClearCachedShadows();

            for (int i = 0; i < vertices.Count(); i++)
            {
                vertices[i] += amount;
                losVertices[i] += amount;
            }

            CalculateDimensions();
        }

        public void SetVertices(Vector2[] points)
        {
            ClearCachedShadows();

            vertices = points;
            losVertices = points;

            int margin = 0;

            if (Math.Abs(points[0].X - points[2].X) < Math.Abs(points[0].Y - points[1].Y))
            {
                losVertices = new Vector2[] {
                    new Vector2(points[0].X+margin, points[0].Y), 
                    new Vector2(points[1].X+margin, points[1].Y), 
                     new Vector2(points[2].X-margin, points[2].Y), 
                    new Vector2(points[3].X-margin, points[3].Y)};
            }
            else
            {
                losVertices = new Vector2[] {
                    new Vector2(points[0].X, points[0].Y +margin), 
                    new Vector2(points[1].X, points[1].Y - margin), 
                     new Vector2(points[2].X, points[2].Y - margin), 
                    new Vector2(points[3].X, points[3].Y + margin)};
            }

            CalculateDimensions();
        }

        private void RemoveCachedShadow(Lights.LightSource light)
        {
            CachedShadow shadow = null;
            cachedShadows.TryGetValue(light, out shadow);

            if (shadow != null)
            {
                shadow.Dispose();
                cachedShadows.Remove(light);
            }
        }

        private void ClearCachedShadows()
        {
            foreach (KeyValuePair<LightSource, CachedShadow> cachedShadow in cachedShadows)
            {
                cachedShadow.Key.NeedsHullUpdate();
                cachedShadow.Value.Dispose();
            }
            cachedShadows.Clear();
        }

        public bool Intersects(Rectangle rect)
        {
            if (!Enabled) return false;

            Rectangle transformedBounds = boundingBox;
            if (parentEntity != null && parentEntity.Submarine != null)
            {
                transformedBounds.X += (int)parentEntity.Submarine.Position.X;
                transformedBounds.Y += (int)parentEntity.Submarine.Position.Y;
            }
            return transformedBounds.Intersects(rect);
        }

        private void CalculateShadowVertices(Vector2 lightSourcePos, bool los = true)
        {
            shadowVertexCount = 0;

            var vertices = los ? losVertices : this.vertices;
            
            //compute facing of each edge, using N*L
            for (int i = 0; i < primitiveCount; i++)
            {
                if (ignoreEdge[i])
                {
                    backFacing[i] = false;
                    continue;
                }

                Vector2 firstVertex = new Vector2(vertices[i].X, vertices[i].Y);
                int secondIndex = (i + 1) % primitiveCount;
                Vector2 secondVertex = new Vector2(vertices[secondIndex].X, vertices[secondIndex].Y);
                Vector2 middle = (firstVertex + secondVertex) / 2;

                Vector2 L = lightSourcePos - middle;

                Vector2 N = new Vector2(
                    -(secondVertex.Y - firstVertex.Y),
                    secondVertex.X - firstVertex.X);

                backFacing[i] = (Vector2.Dot(N, L) < 0) == los;
            }

            //find beginning and ending vertices which
            //belong to the shadow
            int startingIndex = 0;
            int endingIndex = 0;
            for (int i = 0; i < primitiveCount; i++)
            {
                int currentEdge = i;
                int nextEdge = (i + 1) % primitiveCount;

                if (backFacing[currentEdge] && !backFacing[nextEdge])
                    endingIndex = nextEdge;

                if (!backFacing[currentEdge] && backFacing[nextEdge])
                    startingIndex = nextEdge;
            }

            //nr of vertices that are in the shadow
            if (endingIndex > startingIndex)
                shadowVertexCount = endingIndex - startingIndex + 1;
            else
                shadowVertexCount = primitiveCount + 1 - startingIndex + endingIndex;

            //shadowVertices = new VertexPositionColor[shadowVertexCount * 2];

            //create a triangle strip that has the shape of the shadow
            int currentIndex = startingIndex;
            int svCount = 0;
            while (svCount != shadowVertexCount * 2)
            {
                Vector3 vertexPos = new Vector3(vertices[currentIndex], 0.0f);

                int i = los ? svCount : svCount + 1;
                int j = los ? svCount + 1 : svCount;

                //one vertex on the hull
                shadowVertices[i] = new VertexPositionColor();
                shadowVertices[i].Color = los ? Color.Black : Color.Transparent;
                shadowVertices[i].Position = vertexPos;

                //one extruded by the light direction
                shadowVertices[j] = new VertexPositionColor();
                shadowVertices[j].Color = shadowVertices[i].Color;


                Vector3 L2P = vertexPos - new Vector3(lightSourcePos, 0);
                L2P.Normalize();
                
                shadowVertices[j].Position = new Vector3(lightSourcePos, 0) + L2P * 9000;

                svCount += 2;
                currentIndex = (currentIndex + 1) % primitiveCount;
            }

            if (los)
            {
                CalculatePenumbraVertices(startingIndex, endingIndex, lightSourcePos, los);
            }
        }

        private void CalculatePenumbraVertices(int startingIndex, int endingIndex, Vector2 lightSourcePos, bool los)
        {
            for (int n = 0; n < 4; n += 3)
            {
                Vector3 penumbraStart = new Vector3((n == 0) ? vertices[startingIndex] : vertices[endingIndex], 0.0f);

                penumbraVertices[n] = new VertexPositionTexture();
                penumbraVertices[n].Position = penumbraStart;
                penumbraVertices[n].TextureCoordinate = new Vector2(0.0f, 1.0f);
                //penumbraVertices[0].te = fow ? Color.Black : Color.Transparent;

                for (int i = 0; i < 2; i++)
                {
                    penumbraVertices[n + i + 1] = new VertexPositionTexture();
                    Vector3 vertexDir = penumbraStart - new Vector3(lightSourcePos, 0);
                    vertexDir.Normalize();

                    Vector3 normal = (i == 0) ? new Vector3(-vertexDir.Y, vertexDir.X, 0.0f) : new Vector3(vertexDir.Y, -vertexDir.X, 0.0f) * 0.05f;
                    if (n > 0) normal = -normal;

                    vertexDir = penumbraStart - (new Vector3(lightSourcePos, 0) - normal * 20.0f);
                    vertexDir.Normalize();
                    penumbraVertices[n + i + 1].Position = new Vector3(lightSourcePos, 0) + vertexDir * 9000;

                    if (los)
                    {
                        penumbraVertices[n + i + 1].TextureCoordinate = (i == 0) ? new Vector2(0.05f, 0.0f) : new Vector2(1.0f, 0.0f);
                    }
                    else
                    {
                        penumbraVertices[n + i + 1].TextureCoordinate = (i == 0) ? new Vector2(1.0f, 0.0f) : Vector2.Zero;
                    }
                }

                if (n > 0)
                {
                    var temp = penumbraVertices[4];
                    penumbraVertices[4] = penumbraVertices[5];
                    penumbraVertices[5] = temp;
                }
            }
        }

        public void DrawShadows(GraphicsDevice graphicsDevice, Camera cam, LightSource light, Matrix transform, bool los = true)
        {
            if (!Enabled) return;

            Vector2 lightSourcePos = light.Position;

            if (parentEntity != null && parentEntity.Submarine != null)
            {
                if (light.Submarine == null)
                {
                    lightSourcePos -= parentEntity.Submarine.Position;
                }
                else if (light.Submarine != parentEntity.Submarine)
                {
                    lightSourcePos += (light.Submarine.Position-parentEntity.Submarine.Position);
                }
                
            }

            CachedShadow cachedShadow = null;
            if (!cachedShadows.TryGetValue(light, out cachedShadow) ||
                Vector2.DistanceSquared(lightSourcePos, cachedShadow.LightPos) > 1.0f)
            {
                CalculateShadowVertices(lightSourcePos, los);

                if (cachedShadow != null)
                {
                    cachedShadow.LightPos = lightSourcePos;
                    cachedShadow.ShadowBuffer.SetData(shadowVertices, 0, shadowVertices.Length);
                    cachedShadow.ShadowVertexCount = shadowVertexCount;
                }
                else
                {
                    cachedShadow = new CachedShadow(shadowVertices, lightSourcePos, shadowVertexCount, 0);
                    RemoveCachedShadow(light);
                    cachedShadows.Add(light, cachedShadow);
                }
            }

            graphicsDevice.SetVertexBuffer(cachedShadow.ShadowBuffer);
            shadowVertexCount = cachedShadow.ShadowVertexCount;

            DrawShadows(graphicsDevice, cam, transform, los);
        }

        public void DrawShadows(GraphicsDevice graphicsDevice, Camera cam, Vector2 lightSourcePos, Matrix transform, bool los = true)
        {
            if (!Enabled) return;

            if (parentEntity != null && parentEntity.Submarine != null) lightSourcePos -= parentEntity.Submarine.Position;

            CalculateShadowVertices(lightSourcePos, los);

            DrawShadows(graphicsDevice, cam, transform, los);
        }

        private void DrawShadows(GraphicsDevice graphicsDevice, Camera cam, Matrix transform, bool los = true)
        {
            Vector3 offset = Vector3.Zero;
            if (parentEntity != null && parentEntity.Submarine != null)
            {
                offset = new Vector3(parentEntity.Submarine.DrawPosition.X, parentEntity.Submarine.DrawPosition.Y, 0.0f);
            }
            
            if (shadowVertexCount>0)
            {                
                shadowEffect.World = Matrix.CreateTranslation(offset) * transform;                

                if (los)
                {
                    shadowEffect.CurrentTechnique.Passes[0].Apply();
                    graphicsDevice.DrawUserPrimitives(PrimitiveType.TriangleStrip, shadowVertices, 0, shadowVertexCount * 2 - 2, VertexPositionColor.VertexDeclaration);
                }
                else
                {                    
                    shadowEffect.CurrentTechnique.Passes[0].Apply();
                    graphicsDevice.DrawPrimitives(PrimitiveType.TriangleStrip, 0, shadowVertexCount * 2 - 2);
                }               
            
            }


            if (los)
            {
                penumbraEffect.World = shadowEffect.World;
                penumbraEffect.CurrentTechnique.Passes[0].Apply();

#if WINDOWS
                graphicsDevice.DrawUserPrimitives(PrimitiveType.TriangleList, penumbraVertices, 0, 2, VertexPositionTexture.VertexDeclaration);
#endif
            }

        }

        public void Remove()
        {
            ClearCachedShadows();

            list.Remove(this);
        }


    }

}
