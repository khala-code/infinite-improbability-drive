// CosmologicalConstants.cs
// Single source of truth for all physical constants in the ΩaZaTa framework.
// Every hardcoded number in the codebase traces back to here.

namespace InfiniteImprobability.Core
{
    public static class CosmologicalConstants
    {
        // -----------------------------------------------------------------------
        // Fundamental physical constants
        // -----------------------------------------------------------------------

        /// <summary>Speed of light — the hard limit on all navigation (km/s)</summary>
        public const float C_KM_S = 299792.458f;

        /// <summary>Planck time — the quantum proper-time tick (seconds)</summary>
        public const double TAU_PLANCK = 5.391247e-44;

        /// <summary>Boltzmann constant (eV/K)</summary>
        public const float K_BOLTZMANN_EV = 8.617333e-5f;

        // -----------------------------------------------------------------------
        // CMB / photon decoupling — outer boundary
        // z_dec = (T_dec / T_cmb_today) - 1
        // = (3000 / 2.725) - 1 ≈ 1089.80
        // Planck 2018 measurement: 1089.80 ± 0.21
        // -----------------------------------------------------------------------

        /// <summary>CMB temperature today (Kelvin)</summary>
        public const float T_CMB_TODAY_K = 2.725f;

        /// <summary>Photon decoupling temperature (Kelvin)</summary>
        public const float T_PHOTON_DECOUPLING_K = 3000f;

        /// <summary>
        /// Redshift of photon decoupling — the outer boundary of the observer bubble.
        /// The CMB surface. Ξ = Ξ_c here. Beyond this, no classical observer bubble exists.
        /// </summary>
        public const float Z_PHOTON_DECOUPLING = 1089.80f;

        // -----------------------------------------------------------------------
        // Neutrino decoupling — deep outer boundary
        // The neutrino shell lies inside the photon shell (earlier / higher z).
        // -----------------------------------------------------------------------

        /// <summary>Neutrino decoupling temperature (Kelvin)</summary>
        public const float T_NEUTRINO_DECOUPLING_K = 1e10f;

        /// <summary>Neutrino background temperature today = (4/11)^(1/3) × T_CMB (Kelvin)</summary>
        public const float T_NEUTRINO_TODAY_K = 1.945f;

        /// <summary>Redshift of neutrino decoupling — the deep outer boundary (neutrino layer)</summary>
        public const float Z_NEUTRINO_DECOUPLING = 6e9f;

        // -----------------------------------------------------------------------
        // Ξ (Xi) coherence thresholds
        // Ξ is a resource scalar on [0, 1].
        // Below Ξ_CRITICAL the observer bubble cannot maintain classical structure.
        // -----------------------------------------------------------------------

        /// <summary>
        /// Critical coherence threshold — the first symmetry break.
        /// Below this, no stable Za–Ta–Oa frame is possible.
        /// Physically corresponds to photon decoupling (z ≈ 1090).
        /// </summary>
        public const float XI_CRITICAL = 0.15f;

        /// <summary>
        /// Baseline positive trust floor.
        /// The bubble always recovers toward this value when no stress is applied.
        /// Constructive interference is the default state.
        /// </summary>
        public const float XI_BASELINE = 0.72f;

        /// <summary>Rate at which Ξ drifts back to XI_BASELINE (per proper-time tick)</summary>
        public const float XI_RECOVERY_RATE = 0.005f;

        /// <summary>Rate at which Ξ drains per unit of information/energy/mass density</summary>
        public const float XI_DRAIN_RATE = 0.012f;

        // -----------------------------------------------------------------------
        // Ω (Omega) winding number — fidelity scale
        // -----------------------------------------------------------------------

        /// <summary>Minimum Ω — local neighbourhood fidelity (solar system scale)</summary>
        public const int OMEGA_MIN = 1;

        /// <summary>Maximum Ω — full observable universe fidelity</summary>
        public const int OMEGA_MAX = 12;

        /// <summary>Default starting Ω for a new observer session</summary>
        public const int OMEGA_DEFAULT = 4;

        // -----------------------------------------------------------------------
        // Time tax — movement cost proportional to density × dimensional geometry
        // Δt_subjective = Δτ_proper - tax(density, Ω, geometry)
        // -----------------------------------------------------------------------

        /// <summary>Base time tax coefficient (linear term)</summary>
        public const float TAX_LINEAR_COEFF = 0.08f;

        /// <summary>Non-linear time tax coefficient (density² term)</summary>
        public const float TAX_NONLINEAR_COEFF = 0.003f;

        /// <summary>Dimensional geometry tax multiplier per extra dimension navigated</summary>
        public const float TAX_DIMENSION_MULTIPLIER = 0.15f;

        // -----------------------------------------------------------------------
        // BAO / large scale structure
        // -----------------------------------------------------------------------

        /// <summary>BAO sound horizon scale — comoving (Mpc)</summary>
        public const float R_BAO_MPC = 147.09f;

        /// <summary>Hubble constant H0 (km/s/Mpc) — Planck 2018</summary>
        public const float H0 = 67.4f;

        // -----------------------------------------------------------------------
        // Navigation
        // -----------------------------------------------------------------------

        /// <summary>Redshift of the present — inner boundary anchor</summary>
        public const float Z_NOW = 0f;

        /// <summary>
        /// Logarithmic z ladder — the discrete steps the observer can
        /// navigate along the redshift axis. Chosen to land on
        /// cosmologically meaningful epochs.
        /// </summary>
        public static readonly float[] Z_LADDER = new float[]
        {
            0f,       // Now
            0.1f,     // z~0.1 — nearby universe, Hubble flow
            0.5f,     // z~0.5 — 5 Gyr ago
            1.0f,     // z~1   — 8 Gyr ago, peak star formation
            2.0f,     // z~2   — cosmic noon, peak AGN
            5.0f,     // z~5   — reionisation epoch
            10.0f,    // z~10  — first galaxies
            100.0f,   // z~100 — matter-radiation equality
            1089.80f  // z~1090 — CMB / photon decoupling (outer boundary)
        };

        // -----------------------------------------------------------------------
        // Bloch equation / spin dynamics
        // -----------------------------------------------------------------------

        /// <summary>Gyromagnetic ratio γ for the Larmor precession term</summary>
        public const float GAMMA_LARMOR = 1.0f; // normalised; rescale per use case

        /// <summary>Noise injection amplitude η(t) — quantum foam background</summary>
        public const float ETA_NOISE_AMPLITUDE = 0.02f;

        /// <summary>Energy injected per tick at full alignment (S·û = 1)</summary>
        public const float E_TICK_MAX = 1.0f; // normalised
    }
}
