﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Xml.Linq;
using Barotrauma.Abilities;

namespace Barotrauma
{
    class CharacterTalent
    {
        public Character Character { get; }
        public string DebugIdentifier { get; }

        public readonly TalentPrefab Prefab;

        public bool AddedThisRound = true;

        private readonly Dictionary<AbilityEffectType, List<CharacterAbilityGroupEffect>> characterAbilityGroupEffectDictionary = new Dictionary<AbilityEffectType, List<CharacterAbilityGroupEffect>>();

        private readonly List<CharacterAbilityGroupInterval> characterAbilityGroupIntervals = new List<CharacterAbilityGroupInterval>();

        // works functionally but a missing recipe is not represented on GUI side. this might be better placed in the character class itself, though it might be fine here as well
        public List<string> UnlockedRecipes { get; } = new List<string>();

        public CharacterTalent(TalentPrefab talentPrefab, Character character)
        {
            Character = character;

            Prefab = talentPrefab;
            XElement element = talentPrefab.ConfigElement;
            DebugIdentifier = talentPrefab.OriginalName;

            foreach (XElement subElement in element.Elements())
            {
                switch (subElement.Name.ToString().ToLowerInvariant())
                {
                    case "abilitygroupeffect":
                        LoadAbilityGroupEffect(subElement);
                        break;
                    case "abilitygroupinterval":
                        LoadAbilityGroupInterval(subElement);
                        break;
                    case "addedrecipe":
                        if (subElement.GetAttributeString("itemidentifier", string.Empty) is string recipeIdentifier && recipeIdentifier != string.Empty)
                        {
                            UnlockedRecipes.Add(recipeIdentifier);
                        }
                        else
                        {
                            DebugConsole.ThrowError("No recipe identifier defined for talent " + DebugIdentifier);
                        }
                        break;
                }
            }
        }

        public virtual void UpdateTalent(float deltaTime)
        {
            foreach (var characterAbilityGroupInterval in characterAbilityGroupIntervals)
            {
                characterAbilityGroupInterval.UpdateAbilityGroup(deltaTime);
            }
        }

        public void CheckTalent(AbilityEffectType abilityEffectType, AbilityObject abilityObject)
        {
            if (characterAbilityGroupEffectDictionary.TryGetValue(abilityEffectType, out var characterAbilityGroups))
            {
                foreach (var characterAbilityGroup in characterAbilityGroups)
                {
                    characterAbilityGroup.CheckAbilityGroup(abilityObject);
                }
            }
        }

        public void ActivateTalent(bool addingFirstTime)
        {
            foreach (var characterAbilityGroups in characterAbilityGroupEffectDictionary.Values)
            {
                foreach (var characterAbilityGroup in characterAbilityGroups)
                {
                    characterAbilityGroup.ActivateAbilityGroup(addingFirstTime);
                }
            }
        }

        // XML logic
        private void LoadAbilityGroupInterval(XElement abilityGroup)
        {
            characterAbilityGroupIntervals.Add(new CharacterAbilityGroupInterval(AbilityEffectType.Undefined, this, abilityGroup));
        }

        private void LoadAbilityGroupEffect(XElement abilityGroup)
        {
            AbilityEffectType abilityEffectType = ParseAbilityEffectType(this, abilityGroup.GetAttributeString("abilityeffecttype", "none"));
            AddAbilityGroupEffect(new CharacterAbilityGroupEffect(abilityEffectType, this, abilityGroup), abilityEffectType);
        }

        public void AddAbilityGroupEffect(CharacterAbilityGroupEffect characterAbilityGroup, AbilityEffectType abilityEffectType = AbilityEffectType.None)
        {
            if (characterAbilityGroupEffectDictionary.TryGetValue(abilityEffectType, out var characterAbilityList))
            {
                characterAbilityList.Add(characterAbilityGroup);
            }
            else
            {
                List<CharacterAbilityGroupEffect> characterAbilityGroups = new List<CharacterAbilityGroupEffect>();
                characterAbilityGroups.Add(characterAbilityGroup);
                characterAbilityGroupEffectDictionary.Add(abilityEffectType, characterAbilityGroups);
            }
        }

        public static AbilityEffectType ParseAbilityEffectType(CharacterTalent characterTalent, string abilityEffectTypeString)
        {
            if (!Enum.TryParse(abilityEffectTypeString, true, out AbilityEffectType abilityEffectType))
            {
                DebugConsole.ThrowError("Invalid ability effect type \"" + abilityEffectTypeString + "\" in CharacterTalent (" + characterTalent.DebugIdentifier + ")");
            }
            if (abilityEffectType == AbilityEffectType.Undefined)
            {
                DebugConsole.ThrowError("Ability effect type not defined in CharacterTalent (" + characterTalent.DebugIdentifier + ")");
            }

            return abilityEffectType;
        }
    }
}
