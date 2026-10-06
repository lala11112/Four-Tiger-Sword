using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;

namespace ImpossibleRobert.Common
{
    /// <summary>
    /// Reusable retained-mode triangle builder for custom UI Toolkit canvases.
    /// Geometry is emitted as independent triangles so large batches can be split safely.
    /// </summary>
    public sealed class CommonUITKMeshBuilder
    {
        private const int MaxVerticesPerBatch = 60000;

        private readonly List<Vertex> _vertices;

        public int VertexCount => _vertices.Count;

        public CommonUITKMeshBuilder(int initialCapacity = 2048)
        {
            _vertices = new List<Vertex>(Mathf.Max(6, initialCapacity));
        }

        public void Clear()
        {
            _vertices.Clear();
        }

        public void AddRect(Rect rect, Color color)
        {
            AddQuad(
                new Vector2(rect.xMin, rect.yMin),
                new Vector2(rect.xMax, rect.yMin),
                new Vector2(rect.xMax, rect.yMax),
                new Vector2(rect.xMin, rect.yMax),
                color);
        }

        public void AddRectOutline(Rect rect, float width, Color color)
        {
            float clampedWidth = Mathf.Max(0.5f, width);
            AddRect(new Rect(rect.xMin, rect.yMin, rect.width, clampedWidth), color);
            AddRect(new Rect(rect.xMin, rect.yMax - clampedWidth, rect.width, clampedWidth), color);
            AddRect(new Rect(rect.xMin, rect.yMin + clampedWidth, clampedWidth, Mathf.Max(0f, rect.height - clampedWidth * 2f)), color);
            AddRect(new Rect(rect.xMax - clampedWidth, rect.yMin + clampedWidth, clampedWidth, Mathf.Max(0f, rect.height - clampedWidth * 2f)), color);
        }

        public void AddLine(Vector2 start, Vector2 end, float width, Color color)
        {
            Vector2 delta = end - start;
            float magnitude = delta.magnitude;
            if (magnitude <= 0.001f) return;

            Vector2 normal = new Vector2(-delta.y, delta.x) * (Mathf.Max(0.5f, width) * 0.5f / magnitude);
            AddQuad(start - normal, end - normal, end + normal, start + normal, color);
        }

        public void AddBezier(Vector2 start, Vector2 control, Vector2 end, float width, Color color, int segmentCount = 16)
        {
            int segments = Mathf.Clamp(segmentCount, 4, 64);
            Vector2 previous = start;
            for (int i = 1; i <= segments; i++)
            {
                float t = (float)i / segments;
                float inverse = 1f - t;
                Vector2 current = inverse * inverse * start + 2f * inverse * t * control + t * t * end;
                AddLine(previous, current, width, color);
                previous = current;
            }
        }

        public void AddTriangle(Vector2 first, Vector2 second, Vector2 third, Color color)
        {
            AddVertex(first, color);
            AddVertex(second, color);
            AddVertex(third, color);
        }

        public void AddCircle(Vector2 center, float radius, Color color, int segmentCount = 20)
        {
            int segments = Mathf.Clamp(segmentCount, 8, 64);
            Vector2 previous = center + new Vector2(radius, 0f);
            for (int i = 1; i <= segments; i++)
            {
                float angle = Mathf.PI * 2f * i / segments;
                Vector2 current = center + new Vector2(Mathf.Cos(angle), Mathf.Sin(angle)) * radius;
                AddTriangle(center, previous, current, color);
                previous = current;
            }
        }

        public void Flush(MeshGenerationContext context)
        {
            int offset = 0;
            while (offset < _vertices.Count)
            {
                int count = Mathf.Min(MaxVerticesPerBatch, _vertices.Count - offset);
                count -= count % 3;
                if (count <= 0) break;

                MeshWriteData data = context.Allocate(count, count);
                if (data.vertexCount < count) break;

                for (int i = 0; i < count; i++)
                {
                    data.SetNextVertex(_vertices[offset + i]);
                    data.SetNextIndex((ushort)i);
                }

                offset += count;
            }
        }

        private void AddQuad(Vector2 topLeft, Vector2 topRight, Vector2 bottomRight, Vector2 bottomLeft, Color color)
        {
            AddTriangle(topLeft, topRight, bottomRight, color);
            AddTriangle(topLeft, bottomRight, bottomLeft, color);
        }

        private void AddVertex(Vector2 position, Color color)
        {
            _vertices.Add(new Vertex
            {
                position = new Vector3(position.x, position.y, Vertex.nearZ),
                tint = color
            });
        }
    }
}
