using UnityEngine;
using System.Runtime.InteropServices;

namespace Kodai.Fluid.SPH {
    public struct FluidParticle {
        public Vector3 Position;
        public Vector3 Velocity;
    };

    public class Fluid3D : FluidBase<FluidParticle> {
        
        [SerializeField] private float ballRadius = 0.1f;           // 粒子位置初期化時の円半径
        [SerializeField] private float MouseInteractionRadius = 1f; // マウスインタラクションの範囲の広さ
        
        private bool isMouseDown;
        private Vector3 screenToWorldPointPos;

        /// <summary>
        /// パーティクル初期位置の設定
        /// </summary>
        /// <param name="particles"></param>
        protected override void InitParticleData(ref FluidParticle[] particles) {
            for (int i = 0; i < NumParticles; i++) {
                particles[i].Velocity = Vector3.zero;
                particles[i].Position = range / 2f + Random.insideUnitSphere * ballRadius;  // 円形に粒子を初期化する
            }
        }

        /// <summary>
        /// ComputeShaderの定数バッファに追加する
        /// </summary>
        /// <param name="cs"></param>
        protected override void AdditionalCSParams(ComputeShader cs) {

            if (Input.GetMouseButtonDown(0)) {
                isMouseDown = true;
            }

            if(Input.GetMouseButtonUp(0)) {
                isMouseDown = false;
            }

            if (isMouseDown) {
                Vector3 mousePos = Input.mousePosition;
                RaycastHit hit;
                Physics.Raycast (Camera.main.ScreenPointToRay (mousePos), out hit);
                screenToWorldPointPos = hit.point;
            }

            cs.SetVector("_MousePos", screenToWorldPointPos);
            cs.SetFloat("_MouseRadius", MouseInteractionRadius);
            cs.SetBool("_MouseDown", isMouseDown);
        }

    }
}