using System;
using UnityEngine;

namespace BeastCraft.Progression
{
    /// <summary>
    /// The imported drop tables: <c>Data/Skills/drop-tables.json</c> copied verbatim into an asset
    /// by the Editor importer (menu: Beast Craft/Data/Import Drop Tables), so the game reads the
    /// same <see cref="DropTableData"/> the balance simulator does. Never hand-edit
    /// <see cref="Data"/>; edit the JSON and re-import.
    /// </summary>
    [CreateAssetMenu(menuName = "Beast Craft/Progression/Drop Tables", fileName = "DropTables")]
    public class DropTableSO : ScriptableObject
    {
        /// <summary>The authored tables, as imported.</summary>
        public DropTableData Data = new DropTableData();

        /// <summary>
        /// Builds the runtime <see cref="DropTable"/> with <paramref name="tierOf"/> resolving
        /// material tiers (see <see cref="DropTableBuilder.Build"/>).
        /// </summary>
        public DropTable Build(Func<string, int> tierOf)
        {
            return DropTableBuilder.Build(Data, tierOf);
        }
    }
}
