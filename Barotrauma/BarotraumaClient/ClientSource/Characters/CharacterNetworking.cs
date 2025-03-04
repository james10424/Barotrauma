﻿using Barotrauma.Extensions;
using Barotrauma.Items.Components;
using Barotrauma.Networking;
using Microsoft.Xna.Framework;
using System;
using System.Linq;

namespace Barotrauma
{
    partial class Character
    {
        partial void UpdateNetInput()
        {
            if (GameMain.Client != null)
            {
                if (this != Controlled)
                {
                    if (GameMain.Client.EndCinematic != null && 
                        GameMain.Client.EndCinematic.Running) // Freezes the characters during the ending cinematic
                    {
                        AnimController.Frozen = true;
                        memState.Clear();
                        return;
                    }

                    //freeze AI characters if more than x seconds have passed since last update from the server
                    if (lastRecvPositionUpdateTime < Lidgren.Network.NetTime.Now - NetConfig.FreezeCharacterIfPositionDataMissingDelay)
                    {
                        AnimController.Frozen = true;
                        memState.Clear();
                        //hide after y seconds
                        if (lastRecvPositionUpdateTime < Lidgren.Network.NetTime.Now - NetConfig.DisableCharacterIfPositionDataMissingDelay)
                        {
                            Enabled = false;
                            return;
                        }
                    }
                }
                else
                {
                    var posInfo = new CharacterStateInfo(
                        SimPosition,
                        AnimController.Collider.Rotation,
                        LastNetworkUpdateID,
                        AnimController.TargetDir,
                        SelectedCharacter,
                        SelectedConstruction,
                        AnimController.Anim);

                    memLocalState.Add(posInfo);

                    InputNetFlags newInput = InputNetFlags.None;
                    if (IsKeyDown(InputType.Left)) newInput |= InputNetFlags.Left;
                    if (IsKeyDown(InputType.Right)) newInput |= InputNetFlags.Right;
                    if (IsKeyDown(InputType.Up)) newInput |= InputNetFlags.Up;
                    if (IsKeyDown(InputType.Down)) newInput |= InputNetFlags.Down;
                    if (IsKeyDown(InputType.Run)) newInput |= InputNetFlags.Run;
                    if (IsKeyDown(InputType.Crouch)) newInput |= InputNetFlags.Crouch;
                    if (IsKeyHit(InputType.Select)) newInput |= InputNetFlags.Select; //TODO: clean up the way this input is registered
                    if (IsKeyHit(InputType.Deselect)) newInput |= InputNetFlags.Deselect;
                    if (IsKeyHit(InputType.Health)) newInput |= InputNetFlags.Health;
                    if (IsKeyHit(InputType.Grab)) newInput |= InputNetFlags.Grab;
                    if (IsKeyDown(InputType.Use)) newInput |= InputNetFlags.Use;
                    if (IsKeyDown(InputType.Aim)) newInput |= InputNetFlags.Aim;
                    if (IsKeyDown(InputType.Shoot)) newInput |= InputNetFlags.Shoot;
                    if (IsKeyDown(InputType.Attack)) newInput |= InputNetFlags.Attack;
                    if (IsKeyDown(InputType.Ragdoll)) newInput |= InputNetFlags.Ragdoll;

                    if (AnimController.TargetDir == Direction.Left) newInput |= InputNetFlags.FacingLeft;

                    Vector2 relativeCursorPos = cursorPosition - AimRefPosition;
                    relativeCursorPos.Normalize();
                    UInt16 intAngle = (UInt16)(65535.0 * Math.Atan2(relativeCursorPos.Y, relativeCursorPos.X) / (2.0 * Math.PI));

                    NetInputMem newMem = new NetInputMem
                    {
                        states = newInput,
                        intAim = intAngle
                    };

                    if (FocusedCharacter != null && 
                        FocusedCharacter.CampaignInteractionType != CampaignMode.InteractionType.None && 
                        newMem.states.HasFlag(InputNetFlags.Use))
                    {
                        newMem.interact = FocusedCharacter.ID;
                    }
                    else if (newMem.states.HasFlag(InputNetFlags.Use) && (FocusedCharacter?.IsPet ?? false))
                    {
                        newMem.interact = FocusedCharacter.ID;
                    }
                    else if (focusedItem != null && !CharacterInventory.DraggingItemToWorld &&
                        !newMem.states.HasFlag(InputNetFlags.Grab) && !newMem.states.HasFlag(InputNetFlags.Health))
                    {
                        newMem.interact = focusedItem.ID;
                    }
                    else if (FocusedCharacter != null)
                    {
                        newMem.interact = FocusedCharacter.ID;
                    }

                    memInput.Insert(0, newMem);
                    LastNetworkUpdateID++;
                    if (memInput.Count > 60)
                    {
                        memInput.RemoveRange(60, memInput.Count - 60);
                    }
                }
            }
            else //this == Character.Controlled && GameMain.Client == null
            {
                AnimController.Frozen = false;
            }
        }

        public virtual void ClientWrite(IWriteMessage msg, object[] extraData = null)
        {
            if (extraData != null)
            {
                switch ((NetEntityEvent.Type)extraData[0])
                {
                    case NetEntityEvent.Type.InventoryState:
                        msg.WriteRangedInteger(0, 0, 4);
                        Inventory.ClientWrite(msg, extraData);
                        break;
                    case NetEntityEvent.Type.Treatment:
                        msg.WriteRangedInteger(1, 0, 4);
                        msg.Write(AnimController.Anim == AnimController.Animation.CPR);
                        break;
                    case NetEntityEvent.Type.Status:
                        msg.WriteRangedInteger(2, 0, 4);
                        break;
                    case NetEntityEvent.Type.UpdateTalents:
                        msg.WriteRangedInteger(3, 0, 4);
                        msg.Write((ushort)characterTalents.Count);
                        foreach (var unlockedTalent in characterTalents)
                        {
                            msg.Write(unlockedTalent.Prefab.UIntIdentifier);
                        }
                        break;
                }
            }
            else
            {
                msg.Write((byte)ClientNetObject.CHARACTER_INPUT);

                if (memInput.Count > 60)
                {
                    memInput.RemoveRange(60, memInput.Count - 60);
                }

                msg.Write(LastNetworkUpdateID);
                byte inputCount = Math.Min((byte)memInput.Count, (byte)60);
                msg.Write(inputCount);
                for (int i = 0; i < inputCount; i++)
                {
                    msg.WriteRangedInteger((int)memInput[i].states, 0, (int)InputNetFlags.MaxVal);
                    msg.Write(memInput[i].intAim);
                    if (memInput[i].states.HasFlag(InputNetFlags.Select) ||
                        memInput[i].states.HasFlag(InputNetFlags.Deselect) ||
                        memInput[i].states.HasFlag(InputNetFlags.Use) ||
                        memInput[i].states.HasFlag(InputNetFlags.Health) ||
                        memInput[i].states.HasFlag(InputNetFlags.Grab))
                    {
                        msg.Write(memInput[i].interact);
                    }
                }
            }
            msg.WritePadBits();
        }

        public virtual void ClientRead(ServerNetObject type, IReadMessage msg, float sendingTime)
        {
            switch (type)
            {
                case ServerNetObject.ENTITY_POSITION:
                    bool facingRight = AnimController.Dir > 0.0f;

                    lastRecvPositionUpdateTime = (float)Lidgren.Network.NetTime.Now;

                    AnimController.Frozen = false;
                    Enabled = true;

                    UInt16 networkUpdateID = 0;
                    if (msg.ReadBoolean())
                    {
                        networkUpdateID = msg.ReadUInt16();
                    }
                    else
                    {
                        bool aimInput = msg.ReadBoolean();
                        keys[(int)InputType.Aim].Held = aimInput;
                        keys[(int)InputType.Aim].SetState(false, aimInput);

                        bool shootInput = msg.ReadBoolean();
                        keys[(int)InputType.Shoot].Held = shootInput;
                        keys[(int)InputType.Shoot].SetState(false, shootInput);

                        bool useInput = msg.ReadBoolean();
                        keys[(int)InputType.Use].Held = useInput;
                        keys[(int)InputType.Use].SetState(false, useInput);

                        if (AnimController is HumanoidAnimController)
                        {
                            bool crouching = msg.ReadBoolean();
                            keys[(int)InputType.Crouch].Held = crouching;
                            keys[(int)InputType.Crouch].SetState(false, crouching);
                        }

                        bool attackInput = msg.ReadBoolean();
                        keys[(int)InputType.Attack].Held = attackInput;
                        keys[(int)InputType.Attack].SetState(false, attackInput);

                        double aimAngle = msg.ReadUInt16() / 65535.0 * 2.0 * Math.PI;
                        cursorPosition = AimRefPosition + new Vector2((float)Math.Cos(aimAngle), (float)Math.Sin(aimAngle)) * 500.0f;
                        TransformCursorPos();

                        bool ragdollInput = msg.ReadBoolean();
                        keys[(int)InputType.Ragdoll].Held = ragdollInput;
                        keys[(int)InputType.Ragdoll].SetState(false, ragdollInput);

                        facingRight = msg.ReadBoolean();
                    }

                    bool entitySelected = msg.ReadBoolean();
                    Character selectedCharacter = null;
                    Item selectedItem = null;

                    AnimController.Animation animation = AnimController.Animation.None;
                    if (entitySelected)
                    {
                        ushort characterID = msg.ReadUInt16();
                        ushort itemID = msg.ReadUInt16();
                        selectedCharacter = FindEntityByID(characterID) as Character;
                        selectedItem = FindEntityByID(itemID) as Item;
                        if (characterID != NullEntityID)
                        {
                            bool doingCpr = msg.ReadBoolean();
                            if (doingCpr && SelectedCharacter != null)
                            {
                                animation = AnimController.Animation.CPR;
                            }
                        }
                    }

                    Vector2 pos = new Vector2(
                        msg.ReadSingle(),
                        msg.ReadSingle());
                    float MaxVel = NetConfig.MaxPhysicsBodyVelocity;
                    Vector2 linearVelocity = new Vector2(
                        msg.ReadRangedSingle(-MaxVel, MaxVel, 12),
                        msg.ReadRangedSingle(-MaxVel, MaxVel, 12));
                    linearVelocity = NetConfig.Quantize(linearVelocity, -MaxVel, MaxVel, 12);

                    bool fixedRotation = msg.ReadBoolean();
                    float? rotation = null;
                    float? angularVelocity = null;
                    if (!fixedRotation)
                    {
                        rotation = msg.ReadSingle();
                        float MaxAngularVel = NetConfig.MaxPhysicsBodyAngularVelocity;
                        angularVelocity = msg.ReadRangedSingle(-MaxAngularVel, MaxAngularVel, 8);
                        angularVelocity = NetConfig.Quantize(angularVelocity.Value, -MaxAngularVel, MaxAngularVel, 8);
                    }

                    bool readStatus = msg.ReadBoolean();
                    if (readStatus)
                    {
                        ReadStatus(msg);
                        AIController?.ClientRead(msg);
                    }

                    msg.ReadPadBits();

                    int index = 0;
                    if (GameMain.Client.Character == this && CanMove)
                    {
                        var posInfo = new CharacterStateInfo(
                            pos, rotation,
                            networkUpdateID,
                            facingRight ? Direction.Right : Direction.Left,
                            selectedCharacter, selectedItem, animation);

                        while (index < memState.Count && NetIdUtils.IdMoreRecent(posInfo.ID, memState[index].ID))
                            index++;
                        memState.Insert(index, posInfo);
                    }
                    else
                    {
                        var posInfo = new CharacterStateInfo(
                            pos, rotation,
                            linearVelocity, angularVelocity,
                            sendingTime, facingRight ? Direction.Right : Direction.Left,
                            selectedCharacter, selectedItem, animation);

                        while (index < memState.Count && posInfo.Timestamp > memState[index].Timestamp)
                            index++;
                        memState.Insert(index, posInfo);
                    }

                    break;
                case ServerNetObject.ENTITY_EVENT:
                    int eventType = msg.ReadRangedInteger(0, 13);
                    switch (eventType)
                    {
                        case 0: //NetEntityEvent.Type.InventoryState
                            if (Inventory == null)
                            {
                                string errorMsg = "Received an inventory update message for an entity with no inventory (" + Name + ", removed: " + Removed + ")";
                                DebugConsole.ThrowError(errorMsg);
                                GameAnalyticsManager.AddErrorEventOnce("CharacterNetworking.ClientRead:NoInventory" + ID, GameAnalyticsSDK.Net.EGAErrorSeverity.Error, errorMsg);

                                //read anyway to prevent messing up reading the rest of the message
                                _ = msg.ReadUInt16();
                                byte inventoryItemCount = msg.ReadByte();
                                for (int i = 0; i < inventoryItemCount; i++)
                                {
                                    msg.ReadUInt16();
                                }
                            }
                            else
                            {
                                Inventory.ClientRead(type, msg, sendingTime);
                            }
                            break;
                        case 1: //NetEntityEvent.Type.Control
                            byte ownerID = msg.ReadByte();
                            ResetNetState();
                            if (ownerID == GameMain.Client.ID)
                            {
                                if (controlled != null)
                                {
                                    LastNetworkUpdateID = controlled.LastNetworkUpdateID;
                                }

                                if (!IsDead) { Controlled = this; }
                                IsRemotePlayer = false;
                                GameMain.Client.HasSpawned = true;
                                GameMain.Client.Character = this;
                                GameMain.LightManager.LosEnabled = true;
                                GameMain.LightManager.LosAlpha = 1f;
                                GameMain.Client.WaitForNextRoundRespawn = null;
                            }
                            else
                            {
                                if (controlled == this)
                                {
                                    Controlled = null;
                                    IsRemotePlayer = ownerID > 0;
                                }
                            }
                            break;
                        case 2: //NetEntityEvent.Type.Status
                            ReadStatus(msg);
                            break;
                        case 3: //NetEntityEvent.Type.UpdateSkills
                            int skillCount = msg.ReadByte();
                            for (int i = 0; i < skillCount; i++)
                            {
                                string skillIdentifier = msg.ReadString();
                                float skillLevel = msg.ReadSingle();
                                info?.SetSkillLevel(skillIdentifier, skillLevel);
                            }
                            break;
                        case 4: // NetEntityEvent.Type.SetAttackTarget
                        case 5: //NetEntityEvent.Type.ExecuteAttack
                            int attackLimbIndex = msg.ReadByte();
                            UInt16 targetEntityID = msg.ReadUInt16();
                            int targetLimbIndex = msg.ReadByte();
                            Vector2 targetSimPos = new Vector2(msg.ReadSingle(), msg.ReadSingle());
                            //255 = entity already removed, no need to do anything
                            if (attackLimbIndex == 255 || Removed) { break; }
                            if (attackLimbIndex >= AnimController.Limbs.Length)
                            {
                                DebugConsole.ThrowError($"Received invalid SetAttack/ExecuteAttack message. Limb index out of bounds (character: {Name}, limb index: {attackLimbIndex}, limb count: {AnimController.Limbs.Length})");
                                break;
                            }
                            Limb attackLimb = AnimController.Limbs[attackLimbIndex];
                            Limb targetLimb = null;
                            if (!(FindEntityByID(targetEntityID) is IDamageable targetEntity))
                            {
                                DebugConsole.ThrowError($"Received invalid SetAttack/ExecuteAttack message. Target entity not found (ID {targetEntityID})");
                                break;
                            }
                            if (targetEntity is Character targetCharacter)
                            {
                                if (targetLimbIndex >= targetCharacter.AnimController.Limbs.Length)
                                {
                                    DebugConsole.ThrowError($"Received invalid SetAttack/ExecuteAttack message. Target limb index out of bounds (target character: {targetCharacter.Name}, limb index: {targetLimbIndex}, limb count: {targetCharacter.AnimController.Limbs.Length})");
                                    break;
                                }
                                targetLimb = targetCharacter.AnimController.Limbs[targetLimbIndex];
                            }
                            if (attackLimb?.attack != null && Controlled != this)
                            {
                                if (eventType == 4)
                                {
                                    SetAttackTarget(attackLimb, targetEntity, targetSimPos);
                                    PlaySound(CharacterSound.SoundType.Attack, maxInterval: 3);
                                }
                                else
                                {
                                    attackLimb.ExecuteAttack(targetEntity, targetLimb, out _);
                                }
                            }
                            break;
                        case 6: //NetEntityEvent.Type.AssignCampaignInteraction
                            byte campaignInteractionType = msg.ReadByte();
                            bool requireConsciousness = msg.ReadBoolean();
                            (GameMain.GameSession?.GameMode as CampaignMode)?.AssignNPCMenuInteraction(this, (CampaignMode.InteractionType)campaignInteractionType);
                            RequireConsciousnessForCustomInteract = requireConsciousness;
                            break;
                        case 7: //NetEntityEvent.Type.ObjectiveManagerState
                            // 1 = order, 2 = objective
                            int msgType = msg.ReadRangedInteger(0, 2);
                            if (msgType == 0) { break; }
                            bool validData = msg.ReadBoolean();
                            if (!validData) { break; }
                            if (msgType == 1)
                            {
                                int orderIndex = msg.ReadRangedInteger(0, Order.PrefabList.Count);
                                var orderPrefab = Order.PrefabList[orderIndex];
                                string option = null;
                                if (orderPrefab.HasOptions)
                                {
                                    int optionIndex = msg.ReadRangedInteger(-1, orderPrefab.AllOptions.Length);
                                    if (optionIndex > -1)
                                    {
                                        option = orderPrefab.AllOptions[optionIndex];
                                    }
                                }
                                GameMain.GameSession?.CrewManager?.SetOrderHighlight(this, orderPrefab.Identifier, option);
                            }
                            else if (msgType == 2)
                            {
                                string identifier = msg.ReadString();
                                string option = msg.ReadString();
                                ushort objectiveTargetEntityId = msg.ReadUInt16();
                                var objectiveTargetEntity = FindEntityByID(objectiveTargetEntityId);
                                GameMain.GameSession?.CrewManager?.CreateObjectiveIcon(this, identifier, option, objectiveTargetEntity);
                            }
                            break;
                        case 8: //NetEntityEvent.Type.TeamChange
                            byte newTeamId = msg.ReadByte();
                            ChangeTeam((CharacterTeamType)newTeamId);
                            break;
                        case 9: //NetEntityEvent.Type.AddToCrew
                            GameMain.GameSession.CrewManager.AddCharacter(this);
                            CharacterTeamType teamID = (CharacterTeamType)msg.ReadByte();
                            ushort itemCount = msg.ReadUInt16();
                            for (int i = 0; i < itemCount; i++)
                            {
                                ushort itemID = msg.ReadUInt16();
                                if (!(Entity.FindEntityByID(itemID) is Item item)) { continue; }
                                item.AllowStealing = true;
                                var wifiComponent = item.GetComponent<Items.Components.WifiComponent>();
                                if (wifiComponent != null)
                                {
                                    wifiComponent.TeamID = teamID;
                                }
                            }
                            break;
                        case 10: //NetEntityEvent.Type.UpdateExperience
                            int experienceAmount = msg.ReadInt32();
                            info?.SetExperience(experienceAmount);
                            break;
                        case 11: //NetEntityEvent.Type.UpdateTalents:
                            ushort talentCount = msg.ReadUInt16();
                            for (int i = 0; i < talentCount; i++)
                            {
                                bool addedThisRound = msg.ReadBoolean();
                                UInt32 talentIdentifier = msg.ReadUInt32();
                                GiveTalent(talentIdentifier, addedThisRound);
                            }
                            break;
                        case 12: //NetEntityEvent.Type.UpdateMoney:
                            int moneyAmount = msg.ReadInt32();
                            SetMoney(moneyAmount);
                            break;
                        case 13: //NetEntityEvent.Type.UpdatePermanentStats:
                            byte savedStatValueCount = msg.ReadByte();
                            StatTypes statType = (StatTypes)msg.ReadByte();                       
                            info?.ClearSavedStatValues(statType);                        
                            for (int i = 0; i < savedStatValueCount; i++)
                            {
                                string statIdentifier = msg.ReadString();
                                float statValue = msg.ReadSingle();
                                bool removeOnDeath = msg.ReadBoolean();
                                info?.ChangeSavedStatValue(statType, statValue, statIdentifier, removeOnDeath, setValue: true);
                            }
                            break;

                    }
                    msg.ReadPadBits();
                    break;
            }
        }

        public static Character ReadSpawnData(IReadMessage inc)
        {
            DebugConsole.Log("Reading character spawn data");

            if (GameMain.Client == null) { return null; }

            bool noInfo = inc.ReadBoolean();
            ushort id = inc.ReadUInt16();
            string speciesName = inc.ReadString();
            string seed = inc.ReadString();

            Vector2 position = new Vector2(inc.ReadSingle(), inc.ReadSingle());

            bool enabled = inc.ReadBoolean();

            DebugConsole.Log("Received spawn data for " + speciesName);

            Character character = null;
            if (noInfo)
            {
                try
                {
                    character = Create(speciesName, position, seed, characterInfo: null, id: id, isRemotePlayer: false);
                }
                catch (Exception e)
                {
                    DebugConsole.ThrowError($"Failed to spawn character {speciesName}", e);
                    throw;
                }
                bool containsStatusData = inc.ReadBoolean();
                if (containsStatusData)
                {
                    character.ReadStatus(inc);
                }
            }
            else
            {
                bool hasOwner = inc.ReadBoolean();
                int ownerId = hasOwner ? inc.ReadByte() : -1;
                byte teamID = inc.ReadByte();
                bool hasAi = inc.ReadBoolean();
                string infoSpeciesName = inc.ReadString();

                CharacterInfo info = CharacterInfo.ClientRead(infoSpeciesName, inc);
                try
                {
                    character = Create(speciesName, position, seed, characterInfo: info, id: id, isRemotePlayer: ownerId > 0 && GameMain.Client.ID != ownerId, hasAi: hasAi);
                }
                catch (Exception e)
                {
                    DebugConsole.ThrowError($"Failed to spawn character {speciesName}", e);
                    throw;
                }
                character.TeamID = (CharacterTeamType)teamID;
                character.CampaignInteractionType = (CampaignMode.InteractionType)inc.ReadByte();
                if (character.CampaignInteractionType != CampaignMode.InteractionType.None)
                {
                    (GameMain.GameSession.GameMode as CampaignMode)?.AssignNPCMenuInteraction(character, character.CampaignInteractionType);
                }

                // Check if the character has current orders
                int orderCount = inc.ReadByte();
                for (int i = 0; i < orderCount; i++)
                {
                    int orderPrefabIndex = inc.ReadByte();
                    Entity targetEntity = FindEntityByID(inc.ReadUInt16());
                    Character orderGiver = inc.ReadBoolean() ? FindEntityByID(inc.ReadUInt16()) as Character : null;
                    int orderOptionIndex = inc.ReadByte();
                    int orderPriority = inc.ReadByte();
                    OrderTarget targetPosition = null;
                    if (inc.ReadBoolean())
                    {
                        var x = inc.ReadSingle();
                        var y = inc.ReadSingle();
                        var hull = FindEntityByID(inc.ReadUInt16()) as Hull;
                        targetPosition = new OrderTarget(new Vector2(x, y), hull, creatingFromExistingData: true);
                    }

                    if (orderPrefabIndex >= 0 && orderPrefabIndex < Order.PrefabList.Count)
                    {
                        var orderPrefab = Order.PrefabList[orderPrefabIndex];
                        var component = orderPrefab.GetTargetItemComponent(targetEntity as Item);
                        if (!orderPrefab.MustSetTarget || (targetEntity != null && component != null) || targetPosition != null)
                        {
                            var order = targetPosition == null ?
                                new Order(orderPrefab, targetEntity, component, orderGiver: orderGiver) :
                                new Order(orderPrefab, targetPosition, orderGiver: orderGiver);
                            character.SetOrder(order,
                                orderOptionIndex >= 0 && orderOptionIndex < orderPrefab.Options.Length ? orderPrefab.Options[orderOptionIndex] : null,
                                orderPriority, orderGiver, speak: false, force: true);
                        }
                        else
                        {
                            DebugConsole.ThrowError("Could not set order \"" + orderPrefab.Identifier + "\" for character \"" + character.Name + "\" because required target entity was not found.");
                        }
                    }
                    else
                    {
                        DebugConsole.ThrowError("Invalid order prefab index - index (" + orderPrefabIndex + ") out of bounds.");
                    }
                }

                bool containsStatusData = inc.ReadBoolean();
                if (containsStatusData)
                {
                    character.ReadStatus(inc);
                }

                if (character.IsHuman && character.TeamID != CharacterTeamType.FriendlyNPC && character.TeamID != CharacterTeamType.None && !character.IsDead)
                {
                    CharacterInfo duplicateCharacterInfo = GameMain.GameSession.CrewManager.GetCharacterInfos().FirstOrDefault(c => c.ID == info.ID);
                    GameMain.GameSession.CrewManager.RemoveCharacterInfo(duplicateCharacterInfo);
                    GameMain.GameSession.CrewManager.AddCharacter(character);
                }

                if (GameMain.Client.ID == ownerId)
                {
                    GameMain.Client.HasSpawned = true;
                    GameMain.Client.Character = character;
                    if (!character.IsDead) { Controlled = character; }

                    GameMain.LightManager.LosEnabled = true;
                    GameMain.LightManager.LosAlpha = 1f;

                    character.memInput.Clear();
                    character.memState.Clear();
                    character.memLocalState.Clear();
                }
            }

            character.Enabled = Controlled == character || enabled;

            return character;
        }

        private void ReadStatus(IReadMessage msg)
        {
            bool isDead = msg.ReadBoolean();
            if (isDead)
            {
                CauseOfDeathType causeOfDeathType = (CauseOfDeathType)msg.ReadRangedInteger(0, Enum.GetValues(typeof(CauseOfDeathType)).Length - 1);
                AfflictionPrefab causeOfDeathAffliction = null;
                if (causeOfDeathType == CauseOfDeathType.Affliction)
                {
                    string afflictionName = msg.ReadString();
                    if (!AfflictionPrefab.Prefabs.ContainsKey(afflictionName))
                    {
                        string errorMsg = $"Error in CharacterNetworking.ReadStatus: affliction not found ({afflictionName})";
                        causeOfDeathType = CauseOfDeathType.Unknown;
                        GameAnalyticsManager.AddErrorEventOnce("CharacterNetworking.ReadStatus:AfflictionIndexOutOfBounts", GameAnalyticsSDK.Net.EGAErrorSeverity.Error, errorMsg);
                    }
                    else
                    {
                        causeOfDeathAffliction = AfflictionPrefab.Prefabs[afflictionName];
                    }
                }
                if (!IsDead)
                {
                    if (causeOfDeathType == CauseOfDeathType.Pressure || causeOfDeathAffliction == AfflictionPrefab.Pressure)
                    {
                        Implode(true);
                    }
                    else
                    {
                        Kill(causeOfDeathType, causeOfDeathAffliction?.Instantiate(1.0f), true);
                    }
                }
            }
            else
            {
                if (IsDead) { Revive(); }
                CharacterHealth.ClientRead(msg);
            }
            byte severedLimbCount = msg.ReadByte();
            for (int i = 0; i < severedLimbCount; i++)
            {
                int severedJointIndex = msg.ReadByte();
                if (severedJointIndex < 0 || severedJointIndex >= AnimController.LimbJoints.Length)
                {
                    string errorMsg = $"Error in CharacterNetworking.ReadStatus: severed joint index out of bounds (index: {severedJointIndex}, joint count: {AnimController.LimbJoints.Length})";
                    GameAnalyticsManager.AddErrorEventOnce("CharacterNetworking.ReadStatus:JointIndexOutOfBounts", GameAnalyticsSDK.Net.EGAErrorSeverity.Error, errorMsg);
                }
                else
                {
                    AnimController.SeverLimbJoint(AnimController.LimbJoints[severedJointIndex]);
                }
            }
        }
    }
}
