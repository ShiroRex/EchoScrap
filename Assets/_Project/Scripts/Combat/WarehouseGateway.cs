using UnityEngine;

namespace EchoScrap.Combat
{
    [RequireComponent(typeof(BoxCollider))]
    public sealed class WarehouseGateway : MonoBehaviour
    {
        public WarehouseTravel Travel;
        public bool FromWarehouse;
        private bool waitingForExit;
        private void OnTriggerStay(Collider other) { OnTriggerEnter(other); }
        private void OnTriggerEnter(Collider other)
        {
            if (waitingForExit) return;
            var player=other.GetComponentInParent<PlayerHealth>();
            if(player!=null && Travel!=null && Travel.TryTravel(FromWarehouse,player))
                waitingForExit = FromWarehouse && Travel.IsInWarehouse;
        }
        private void OnTriggerExit(Collider other)
        {
            if (other.GetComponentInParent<PlayerHealth>() != null) waitingForExit = false;
        }
    }
}
