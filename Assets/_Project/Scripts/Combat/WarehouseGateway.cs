using UnityEngine;

namespace EchoScrap.Combat
{
    [RequireComponent(typeof(BoxCollider))]
    public sealed class WarehouseGateway : MonoBehaviour
    {
        public WarehouseTravel Travel;
        public bool FromWarehouse;
        private void OnTriggerStay(Collider other) { OnTriggerEnter(other); }
        private void OnTriggerEnter(Collider other)
        {
            var player=other.GetComponentInParent<PlayerHealth>();
            if(player!=null && Travel!=null) Travel.TryTravel(FromWarehouse,player);
        }
    }
}
