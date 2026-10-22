using UnityEngine;

namespace EchoScrap.Core
{
    /// <summary>
    /// Capas físicas y matriz de colisiones estable del combate.
    /// Durante el dash, el jugador cambia a PlayerDashLayer (7), que atraviesa EnemyLayer (8)
    /// incluidos Coloso y Gólgora, conservando colisiones con suelo, muros y obstáculos (DefaultLayer 0).
    /// </summary>
    public static class PhysicsLayers
    {
        public const int DefaultLayer = 0;
        public const int PlayerLayer = 6;
        public const int PlayerDashLayer = 7;
        public const int EnemyLayer = 8;

        private static bool configured;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        public static void EnsureCollisionMatrix()
        {
            // Suelo, obstáculos y muros en Default (0) colisionan siempre con Player y PlayerDash
            Physics.IgnoreLayerCollision(PlayerLayer, DefaultLayer, false);
            Physics.IgnoreLayerCollision(PlayerDashLayer, DefaultLayer, false);
            Physics.IgnoreLayerCollision(EnemyLayer, DefaultLayer, false);

            // En estado normal, el jugador colisiona con cuerpos sólidos en EnemyLayer (Coloso, Gólgora)
            Physics.IgnoreLayerCollision(PlayerLayer, EnemyLayer, false);

            // Durante el dash, PlayerDash atraviesa todos los cuerpos enemigos (incluidos Coloso y Gólgora)
            Physics.IgnoreLayerCollision(PlayerDashLayer, EnemyLayer, true);

            configured = true;
        }

        public static void EnsureConfigured()
        {
            if (!configured)
                EnsureCollisionMatrix();
            else
                Physics.IgnoreLayerCollision(PlayerDashLayer, EnemyLayer, true);
        }

        public static void SetLayerRecursively(GameObject root, int layer, Transform excludeSubtree = null)
        {
            if (root == null)
                return;

            root.layer = layer;
            Transform t = root.transform;
            for (int i = 0; i < t.childCount; i++)
            {
                Transform child = t.GetChild(i);
                if (child == excludeSubtree)
                    continue;
                SetLayerRecursively(child.gameObject, layer, excludeSubtree);
            }
        }
    }
}
