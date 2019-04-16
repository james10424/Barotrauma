﻿using Barotrauma.Items.Components;
using Microsoft.Xna.Framework;
using System.Linq;

namespace Barotrauma
{
    class AIObjectiveFindDivingGear : AIObjective
    {
        public override string DebugTag => "find diving gear";
        public override bool ForceRun => true;

        private AIObjective subObjective;
        private string gearTag;

        public override bool IsCompleted()
        {
            for (int i = 0; i < character.Inventory.Items.Length; i++)
            {
                if (character.Inventory.SlotTypes[i] == InvSlotType.Any || character.Inventory.Items[i] == null) { continue; }
                if (character.Inventory.Items[i].HasTag(gearTag))
                {
                    var containedItems = character.Inventory.Items[i].ContainedItems;
                    if (containedItems == null) { continue; }

                    var oxygenTank = containedItems.FirstOrDefault(it => (it.Prefab.Identifier == "oxygentank" || it.HasTag("oxygensource")) && it.Condition > 0.0f);
                    if (oxygenTank != null) { return true; }
                }
            }
            return false;
        }

        public AIObjectiveFindDivingGear(Character character, bool needDivingSuit, float priorityModifier = 1) : base(character, "", priorityModifier)
        {
            gearTag = needDivingSuit ? "divingsuit" : "diving";
        }

        protected override void Act(float deltaTime)
        {
            var item = character.Inventory.FindItemByTag(gearTag);
            if (item == null || !character.HasEquippedItem(item))
            {
                //get a diving mask/suit first
                if (!(subObjective is AIObjectiveGetItem))
                {
                    character.Speak(TextManager.Get("DialogGetDivingGear"), null, 0.0f, "getdivinggear", 30.0f);
                    subObjective = new AIObjectiveGetItem(character, gearTag, true);
                }
            }
            else
            {
                var containedItems = item.ContainedItems;
                if (containedItems == null) { return; }
                //check if there's an oxygen tank in the mask/suit
                foreach (Item containedItem in containedItems)
                {
                    if (containedItem == null) { continue; }
                    if (containedItem.Condition <= 0.0f)
                    {
                        containedItem.Drop(character);
                    }
                    else if (containedItem.Prefab.Identifier == "oxygentank" || containedItem.HasTag("oxygensource"))
                    {
                        //we've got an oxygen source inside the mask/suit, all good
                        return;
                    }
                }               
                if (!(subObjective is AIObjectiveContainItem) || subObjective.IsCompleted())
                {
                    character.Speak(TextManager.Get("DialogGetOxygenTank"), null, 0, "getoxygentank", 30.0f);
                    subObjective = new AIObjectiveContainItem(character, new string[] { "oxygentank", "oxygensource" }, item.GetComponent<ItemContainer>());
                }
            }
            if (subObjective != null)
            {
                subObjective.TryComplete(deltaTime);
            }
        }

        public override bool CanBeCompleted => subObjective == null || subObjective.CanBeCompleted;
        public override float GetPriority(AIObjectiveManager objectiveManager) => MathHelper.Clamp(100 - character.OxygenAvailable, 0, 100);
        public override bool IsDuplicate(AIObjective otherObjective) => otherObjective is AIObjectiveFindDivingGear;
    }
}
