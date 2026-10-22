using UnityEngine;

namespace EchoScrap.Combat
{
    /// <summary>
    /// Último cierre de daños del frame: espera Update y los hazards que se procesan en LateUpdate
    /// (orden 100), sin adelantar la victoria antes de una muerte simultánea del jugador.
    /// </summary>
    [DisallowMultipleComponent]
    [DefaultExecutionOrder(1000)]
    public sealed class CombatOutcomeResolver : MonoBehaviour
    {
        private CombatSession session;
        public void Bind(CombatSession combatSession) => session = combatSession;

        private void LateUpdate()
        {
            if (session != null && session.isActiveAndEnabled)
                session.ResolveSimulationStepOutcome();
        }
    }
}
