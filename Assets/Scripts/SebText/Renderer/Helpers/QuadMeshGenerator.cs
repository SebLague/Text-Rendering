using UnityEngine;

namespace SebText.Rendering.Helpers
{
    public static class QuadMeshGenerator
    {
        public static Mesh GenerateQuadMesh()
        {
            int[] indices = new int[] { 0, 1, 2, 2, 1, 3 };

            Vector3[] vertices = new Vector3[]
            {
                new Vector3(-0.5f, 0.5f),
                new Vector3(0.5f, 0.5f),
                new Vector3(-0.5f, -0.5f),
                new Vector3(0.5f, -0.5f)
            };
            Vector2[] uvs = new Vector2[]
            {
                new Vector2(0,1),
                new Vector2(1,1),
                new Vector2(0,0),
                new Vector2(1,0)
            };

            Mesh mesh = new Mesh();
            mesh.SetVertices(vertices);
            mesh.SetTriangles(indices, 0, true);
            mesh.SetUVs(0, uvs);
            return mesh;
        }
    }
}