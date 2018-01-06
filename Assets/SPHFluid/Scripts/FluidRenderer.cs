using UnityEngine;
using System.Collections;

namespace Kodai.Fluid.SPH {

    [RequireComponent(typeof(Fluid3D))]
    public class FluidRenderer : MonoBehaviour {

        public Fluid3D solver;
        public Material RenderParticleMat;
        public Color color1,color2,color3;

        void OnRenderObject() {
            DrawParticle();
        }

        void DrawParticle() {

            RenderParticleMat.SetPass(0);
            RenderParticleMat.SetColor ("_Color1", color1);
            RenderParticleMat.SetColor ("_Color2", color2);
            RenderParticleMat.SetColor ("_Color3", color3);
            RenderParticleMat.SetBuffer("_ParticlesBuffer", solver.ParticlesBufferRead);
            RenderParticleMat.SetBuffer ("_ParticlesTemperatureBuffer", solver.ParticlesTemperatureBuffer);
            Graphics.DrawProcedural(MeshTopology.Points, solver.NumParticles);
        }
    }
}