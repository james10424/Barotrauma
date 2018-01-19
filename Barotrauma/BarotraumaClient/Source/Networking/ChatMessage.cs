﻿using Lidgren.Network;
using System;

namespace Barotrauma.Networking
{
    partial class ChatMessage
    {
        public virtual void ClientWrite(NetOutgoingMessage msg)
        {
            msg.Write((byte)ClientNetObject.CHAT_MESSAGE);
            msg.Write(NetStateID);
            msg.Write((byte)Type);
            msg.Write(Text);
        }

        public static void ClientRead(NetIncomingMessage msg)
        {
            UInt16 ID = msg.ReadUInt16();
            ChatMessageType type = (ChatMessageType)msg.ReadByte();
            string txt = "";

            if (type != ChatMessageType.Order)
            {
                txt = msg.ReadString();
            }

            string senderName = msg.ReadString();
            Character senderCharacter = null;
            bool hasSenderCharacter = msg.ReadBoolean();
            if (hasSenderCharacter)
            {
                senderCharacter = Entity.FindEntityByID(msg.ReadUInt16()) as Character;
                if (senderCharacter != null)
                {
                    senderName = senderCharacter.Name;
                }
            }

            if (type == ChatMessageType.Order)
            {
                int orderIndex = msg.ReadByte();
                Order order = null;
                if (orderIndex < 0 || orderIndex >= Order.PrefabList.Count)
                {
                    DebugConsole.ThrowError("Invalid order message - order index out of bounds.");
                }
                else
                {
                    order = Order.PrefabList[orderIndex];
                }

                UInt16 targetCharacterID = msg.ReadUInt16();
                Character targetCharacter = Entity.FindEntityByID(targetCharacterID) as Character;
                Entity targetEntity =  Entity.FindEntityByID(msg.ReadUInt16());

                int optionIndex = msg.ReadByte();
                string orderOption = "";
                if (order != null && optionIndex >= 0 && optionIndex < order.Options.Length)
                {
                    orderOption = order.Options[optionIndex];
                }
                txt = order.GetChatMessage(targetCharacter?.Name, senderCharacter?.CurrentHull?.RoomName, orderOption);

                if (order.TargetAllCharacters)
                {
                    GameMain.GameSession?.CrewManager?.AddOrder(
                        new Order(order.Prefab, targetEntity, (targetEntity as Item)?.GetComponent<Items.Components.ItemComponent>()), 
                        order.Prefab.FadeOutTime);
                }
                else if (targetCharacter != null)
                {
                    targetCharacter.SetOrder(
                        new Order(order.Prefab, targetEntity, (targetEntity as Item)?.GetComponent<Items.Components.ItemComponent>()), orderOption);
                }

                if (NetIdUtils.IdMoreRecent(ID, LastID))
                {
                    GameMain.Client.AddChatMessage(txt, type, senderName, senderCharacter);
                    LastID = ID;
                }
            }
            else
            {
                if (NetIdUtils.IdMoreRecent(ID, LastID))
                {
                    if (type == ChatMessageType.MessageBox)
                    {
                        new GUIMessageBox("", txt);
                    }
                    else if (type == ChatMessageType.Console)
                    {
                        DebugConsole.NewMessage(txt, MessageColor[(int)ChatMessageType.Console]);
                    }
                    else
                    {
                        GameMain.Client.AddChatMessage(txt, type, senderName, senderCharacter);
                    }
                    LastID = ID;
                }
            }
        }
    }
}
