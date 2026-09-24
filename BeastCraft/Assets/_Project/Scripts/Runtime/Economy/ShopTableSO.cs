using UnityEngine;

namespace BeastCraft.Economy
{
    /// <summary>
    /// The imported shop tables (menu: Beast Craft/Data/Import Shop Tables). Never hand-edit
    /// <see cref="Data"/>; edit the JSON and re-import.
    /// </summary>
    [CreateAssetMenu(menuName = "Beast Craft/Economy/Shop Tables", fileName = "ShopTables")]
    public class ShopTableSO : ScriptableObject
    {
        public ShopTableData Data = new ShopTableData();
    }
}
