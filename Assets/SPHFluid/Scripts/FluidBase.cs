using System.Collections;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using UnityEngine;

namespace Kodai.Fluid.SPH {

    public enum NumParticleEnum {
        NUM_1K = 1024,
        NUM_2K = 1024 * 2,
        NUM_4K = 1024 * 4,
        NUM_8K = 1024 * 8
    };

    // 構造体の定義
    // コンピュートシェーダ側とバイト幅(アライメント)を合わせる
    struct FluidParticleDensity {
        public float Density;
    };

    struct FluidParticlePressure {
        float pressure;
    };

    struct FluidParticleTemperature {
        float temperature;
        float fuel;
    };

    struct FluidParticleType {
        int type;
    };

    struct FluidParticleForces {
        public Vector3 Acceleration;
    };

    struct FluidParticleThermalDiffuse {
        float thermalDiffuse;
        float fuelDiffuse;
    };

    public abstract class FluidBase<T> : MonoBehaviour where T : struct {

        [SerializeField] protected NumParticleEnum particleNum = NumParticleEnum.NUM_8K;    // パーティクルの個数
        [SerializeField] protected float smoothlen = 0.012f;                                // 粒子半径
        [SerializeField] private float pressureStiffness = 200.0f;                          // 圧力項係数
        [SerializeField] protected float restDensity = 1000.0f;                             // 静止密度
        [SerializeField] protected float particleMass = 0.0002f;                            // 粒子質量
        [SerializeField] protected float particleDist = 170.9976f;                          // 粒子の初期間隔
        [SerializeField] protected float viscosity = 0.1f;                                  // 粘性係数
        [SerializeField] protected float maxAllowableTimestep = 0.005f;                     // 時間刻み幅
        [SerializeField] protected float wallStiffness = 3000.0f;                           // ペナルティ法の壁の力
        [SerializeField] protected int iterations = 4;                                      // シミュレーションの1フレーム当たりのイテレーション回数
        [SerializeField] protected Vector3 gravity = new Vector3 (0.0f, -0.5f, 0.0f);       // 重力
        [SerializeField] protected Vector3 range = new Vector3 (1, 1, 1);                   // シミュレーション空間
        [SerializeField] protected bool simulate = true;                                    // シミュレーション実行 or 一時停止
        [SerializeField] protected bool isFirework = true;                                  // 花火のシミュレーション

        [Header ("Heat")]
        [SerializeField]
        protected float fuelConsumeFactor = 5.0f;                            // 燃料消費係数 1.0
        [SerializeField] protected float reactSpeed = 0.01f;                                // 化学反応速度係数 0.01
        [SerializeField] protected float temperatureProduce = 5.0f;                         // 温度生成係数　(筆者自己定義?) 500.0
        [SerializeField] protected float ambientTemperature = 300.0f;                       // 環境温度
        [SerializeField] protected float emissivity = 0.5f;                                 // 物質の放射率 (0 ≤ emiss ≤ 1)
        [SerializeField] protected float stefanConst = -7.56f * Mathf.Pow (10, -16);        // ステファン・ボルツマン定数(J/m^3K^4) 
        [SerializeField] protected float copperDiffusivity = 100 * Mathf.Pow (10, -6);      // 熱拡散率(銅) 100 * pow(10,-6)
        [SerializeField] protected float fuelDiffuse = 0.001f;                              // 燃料拡散係数
        [SerializeField] protected float starRadius = 0.5f;                                 // 星の半径  
        [SerializeField] protected float burnSpeed = 0.004f;                                // 星の燃焼速度  
        [SerializeField] protected float buoyancyCoef = 3.0f;                               // 浮力係数   



        private int numParticles;                                                           // パーティクルの個数
        private float timeStep;                                                             // 時間刻み幅
        private float densityCoef;                                                          // Poly6カーネルの密度係数
        private float gradPressureCoef;                                                     // Spikyカーネルの圧力係数
        private float lapViscosityCoef;                                                     // Laplacianカーネルの粘性係数

        #region DirectCompute
        private ComputeShader fluidCS;
        private static readonly int THREAD_SIZE_X = 1024;                                   // コンピュートシェーダ側のスレッド数
        private ComputeBuffer particlesBufferRead;                                          // 粒子のデータを保持するバッファ
        private ComputeBuffer particlesBufferWrite;                                         // 粒子のデータを書き込むバッファ
        private ComputeBuffer particlesPressureBuffer;                                      // 粒子の圧力データを保持するバッファ
        private ComputeBuffer particleDensitiesBuffer;                                      // 粒子の密度データを保持するバッファ
        private ComputeBuffer particleForcesBuffer;                                         // 粒子の加速度データを保持するバッファ
        private ComputeBuffer particlesTemperatureBuffer;                                   // 粒子の温度データを保持するバッファ
        private ComputeBuffer particlesInitPosBuffer;                                       // 粒子の初期位置データを保持するバッファ
        private ComputeBuffer particlesTypeBuffer;                                          // 粒子の燃焼判定データを保持するバッファ
        private ComputeBuffer particleThermalDiffuseBuffer;                                 // 粒子の熱拡散データを保持するバッファ
        #endregion

        #region Accessor
        public int NumParticles {
            get { return numParticles; }
        }

        public ComputeBuffer ParticlesBufferRead {
            get { return particlesBufferRead; }
        }

        public ComputeBuffer ParticlesTemperatureBuffer {
            get { return particlesTemperatureBuffer; }
        }
        #endregion

        #region Mono
        protected virtual void Awake () {
            fluidCS = (ComputeShader)Resources.Load ("SPH3D");
            numParticles = (int)particleNum;
        }

        protected virtual void Start () {
            InitBuffers ();
        }

        private void Update () {

            if (!simulate) {
                return;
            }

            timeStep = Mathf.Min (maxAllowableTimestep, Time.deltaTime);

            // 2Dカーネル係数
            densityCoef = particleMass * 4f / (Mathf.PI * Mathf.Pow (smoothlen, 8));
            gradPressureCoef = particleMass * -30.0f / (Mathf.PI * Mathf.Pow (smoothlen, 5));
            lapViscosityCoef = particleMass * 20f / (3 * Mathf.PI * Mathf.Pow (smoothlen, 5));

            // シェーダー定数の転送
            fluidCS.SetInt ("_NumParticles", numParticles);
            fluidCS.SetBool ("_IsFirework", isFirework);
            fluidCS.SetFloat ("_TimeStep", timeStep);
            fluidCS.SetFloat ("_Smoothlen", smoothlen);
            fluidCS.SetFloat ("_PressureStiffness", pressureStiffness);
            fluidCS.SetFloat ("_RestDensity", restDensity);
            fluidCS.SetFloat ("_ParticleDist", particleDist);
            fluidCS.SetFloat ("_Viscosity", viscosity);
            fluidCS.SetFloat ("_DensityCoef", densityCoef);
            fluidCS.SetFloat ("_GradPressureCoef", gradPressureCoef);
            fluidCS.SetFloat ("_LapViscosityCoef", lapViscosityCoef);
            fluidCS.SetFloat ("_WallStiffness", wallStiffness);

            fluidCS.SetFloat ("_FuelConsumeFactor", fuelConsumeFactor);
            fluidCS.SetFloat ("_ReactSpeed", reactSpeed);
            fluidCS.SetFloat ("_TemperatureProduce", temperatureProduce);
            fluidCS.SetFloat ("_AmbientTemperature", ambientTemperature);
            fluidCS.SetFloat ("_Emissivity", emissivity);
            fluidCS.SetFloat ("_StefanConst", stefanConst);
            fluidCS.SetFloat ("_CopperDiffusivity", copperDiffusivity);
            fluidCS.SetFloat ("_FuelDiffuse", fuelDiffuse);
            fluidCS.SetFloat ("_ParticleMass", particleMass);
            fluidCS.SetFloat ("_StarRadius", starRadius);
            fluidCS.SetFloat ("_BurnSpeed", burnSpeed);
            fluidCS.SetFloat ("_BuoyancyCoef", buoyancyCoef);

            fluidCS.SetVector ("_Range", range);
            fluidCS.SetVector ("_Gravity", gravity);

            AdditionalCSParams (fluidCS);

            // 計算精度を上げるために時間刻み幅を小さくして複数回イテレーションする
            for (int i = 0; i < iterations; i++) {
                RunFluidSolver ();

                starRadius -= burnSpeed; //燃焼速度の更新
            }
        }

        private void OnDestroy () {
            DeleteBuffer (particlesBufferRead);
            DeleteBuffer (particlesBufferWrite);
            DeleteBuffer (particlesPressureBuffer);
            DeleteBuffer (particleDensitiesBuffer);
            DeleteBuffer (particleForcesBuffer);
            DeleteBuffer (particlesTemperatureBuffer);
            DeleteBuffer (particlesInitPosBuffer);
            DeleteBuffer (particleThermalDiffuseBuffer);
            DeleteBuffer (particlesTypeBuffer);
        }
        #endregion Mono

        /// <summary>
        /// 流体シミュレーションメインルーチン
        /// </summary>
        private void RunFluidSolver () {

            int kernelID = -1;
            int threadGroupsX = numParticles / THREAD_SIZE_X;

            // Density
            kernelID = fluidCS.FindKernel ("DensityCS");
            fluidCS.SetBuffer (kernelID, "_ParticlesBufferRead", particlesBufferRead);
            fluidCS.SetBuffer (kernelID, "_ParticlesDensityBufferWrite", particleDensitiesBuffer);
            fluidCS.Dispatch (kernelID, threadGroupsX, 1, 1);

            // Pressure
            kernelID = fluidCS.FindKernel ("PressureCS");
            fluidCS.SetBuffer (kernelID, "_ParticlesDensityBufferRead", particleDensitiesBuffer);
            fluidCS.SetBuffer (kernelID, "_ParticlesPressureBufferWrite", particlesPressureBuffer);
            fluidCS.Dispatch (kernelID, threadGroupsX, 1, 1);

            // Force
            kernelID = fluidCS.FindKernel ("ForceCS");
            fluidCS.SetBuffer (kernelID, "_ParticlesBufferRead", particlesBufferRead);
            fluidCS.SetBuffer (kernelID, "_ParticlesDensityBufferRead", particleDensitiesBuffer);
            fluidCS.SetBuffer (kernelID, "_ParticlesPressureBufferRead", particlesPressureBuffer);
            fluidCS.SetBuffer (kernelID, "_ParticlesForceBufferWrite", particleForcesBuffer);
            fluidCS.Dispatch (kernelID, threadGroupsX, 1, 1);

            if (isFirework) {
                // Thermal Diffuse
                kernelID = fluidCS.FindKernel ("ThermalDiffuseCS");
                fluidCS.SetBuffer (kernelID, "_ParticlesBufferRead", particlesBufferRead);
                fluidCS.SetBuffer (kernelID, "_ParticlesDensityBufferRead", particleDensitiesBuffer);
                fluidCS.SetBuffer (kernelID, "_ParticlesTemperatureBufferRead", particlesTemperatureBuffer);
                fluidCS.SetBuffer (kernelID, "_ParticlesTypeBufferRead", particlesTypeBuffer);
                fluidCS.SetBuffer (kernelID, "_ParticleThermalDiffuseBufferWrite", particleThermalDiffuseBuffer);
                fluidCS.Dispatch (kernelID, threadGroupsX, 1, 1);

                // Type & Temperature
                kernelID = fluidCS.FindKernel ("TypeCS");
                fluidCS.SetBuffer (kernelID, "_ParticlesBufferRead", particlesBufferRead);
                fluidCS.SetBuffer (kernelID, "_ParticlesInitPosBufferRead", particlesInitPosBuffer);
                fluidCS.SetBuffer (kernelID, "_ParticleThermalDiffuseBufferRead", particleThermalDiffuseBuffer);
                fluidCS.SetBuffer (kernelID, "_ParticlesTemperatureBufferWrite", particlesTemperatureBuffer);
                fluidCS.SetBuffer (kernelID, "_ParticlesTypeBufferWrite", particlesTypeBuffer);
                fluidCS.Dispatch (kernelID, threadGroupsX, 1, 1);
            }
            // Integrate
            kernelID = fluidCS.FindKernel ("IntegrateCS");
            fluidCS.SetBuffer (kernelID, "_ParticlesBufferRead", particlesBufferRead);
            fluidCS.SetBuffer (kernelID, "_ParticlesForceBufferRead", particleForcesBuffer);
            fluidCS.SetBuffer (kernelID, "_ParticlesBufferWrite", particlesBufferWrite);
            fluidCS.Dispatch (kernelID, threadGroupsX, 1, 1);

            SwapComputeBuffer (ref particlesBufferRead, ref particlesBufferWrite);   // バッファの入れ替え
        }

        /// <summary>
        /// 子クラスでシェーダー定数の転送を追加する場合はこのメソッドを利用する
        /// </summary>
        /// <param name="shader"></param>
        protected virtual void AdditionalCSParams (ComputeShader shader) { }

        /// <summary>
        /// パーティクル初期位置と初速の設定
        /// </summary>
        /// <param name="particles"></param>
        protected abstract void InitParticleData (ref T[] particles);

        /// <summary>
        /// バッファの初期化
        /// </summary>
        private void InitBuffers () {
            particlesBufferRead = new ComputeBuffer (numParticles, Marshal.SizeOf (typeof (T)));
            particlesInitPosBuffer = new ComputeBuffer (numParticles, Marshal.SizeOf (typeof (T)));
            var particles = new T[numParticles];
            InitParticleData (ref particles);
            particlesBufferRead.SetData (particles);
            particlesInitPosBuffer.SetData (particles);
            particles = null;

            particlesBufferWrite = new ComputeBuffer (numParticles, Marshal.SizeOf (typeof (T)));
            particlesTypeBuffer = new ComputeBuffer (numParticles, Marshal.SizeOf (typeof (FluidParticleType)));
            particlesPressureBuffer = new ComputeBuffer (numParticles, Marshal.SizeOf (typeof (FluidParticlePressure)));
            particleForcesBuffer = new ComputeBuffer (numParticles, Marshal.SizeOf (typeof (FluidParticleForces)));
            particleDensitiesBuffer = new ComputeBuffer (numParticles, Marshal.SizeOf (typeof (FluidParticleDensity)));
            particlesTemperatureBuffer = new ComputeBuffer (numParticles, Marshal.SizeOf (typeof (FluidParticleTemperature)));
            particleThermalDiffuseBuffer = new ComputeBuffer (numParticles, Marshal.SizeOf (typeof (FluidParticleThermalDiffuse)));
        }

        /// <summary>
        /// 引数に指定されたバッファの入れ替え
        /// </summary>
        private void SwapComputeBuffer (ref ComputeBuffer ping, ref ComputeBuffer pong) {
            ComputeBuffer temp = ping;
            ping = pong;
            pong = temp;
        }

        /// <summary>
        /// バッファの開放
        /// </summary>
        /// <param name="buffer"></param>
        private void DeleteBuffer (ComputeBuffer buffer) {
            if (buffer != null) {
                buffer.Release ();
                buffer = null;
            }
        }
    }
}