﻿using System;
using System.Linq;
using System.Linq.Expressions;
using System.Net;
using System.Reflection;
using CitizenFX.Core;
using CitizenFX.Core.Native;
using CloNET;
using DispatchSystem.Common.DataHolders.Storage;
using DispatchSystem.sv.External;

using static DispatchSystem.sv.Common;

namespace DispatchSystem.sv
{
    public partial class DispatchSystem
    {
        #region Custom Events
        public static void DispatchReset(string handle)
        {
            Player p = GetPlayerByHandle(handle);

            if (GetCivilian(p.Handle) != null)
            {
                civs.Remove(GetCivilian(p.Handle));
            }
            if (GetCivilianVeh(p.Handle) != null)
            {
                civVehs.Remove(GetCivilianVeh(p.Handle));
            }
            if (GetOfficer(p.Handle) != null)
            {
                officers.Remove(GetOfficer(p.Handle));
            }

            SendMessage(p, "DispatchSystem", new[] { 0, 0, 0 }, "All profiles reset");
        }

        #region Civilian Events
        public static void SetName(string handle, string first, string last)
        {
            Player p = GetPlayerByHandle(handle);
            if (p == null) return;

            if (GetOfficer(handle) != null)
            {
                SendMessage(p, "DispatchSystem", new[] { 0, 0, 0 }, "You cannot be an officer and a civilian at the same time!");
                return;
            }

            if (GetCivilianByName(first, last) != null && GetPlayerByIp(GetCivilianVeh(handle).SourceIP) != p)
            {
                SendMessage(p, "DispatchSystem", new[] { 0, 0, 0 }, $"That name already exists in the system!");
                return;
            }

            if (GetCivilian(handle) != null)
            {
                int index = civs.IndexOf(GetCivilian(handle));

                civs[index] = new Civilian(p.Identifiers["ip"]) { First = first, Last = last };

                SendMessage(p, "DispatchSystem", new[] { 0, 0, 0 }, $"New name set to: {civs[index].First} {civs[index].Last}");
            }
            else
            {
                civs.Add(new Civilian(p.Identifiers["ip"]) { First = first, Last = last });
                int index = civs.IndexOf(GetCivilian(handle));

                SendMessage(p, "DispatchSystem", new[] { 0, 0, 0 }, $"New name set to: {civs[index].First} {civs[index].Last}");
#if DEBUG
                SendMessage(p, "", new[] { 0, 0, 0 }, "Creating new civilian profile...");
#endif
            }
            if (GetCivilianVeh(handle) != null)
            {
                int index = civVehs.IndexOf(GetCivilianVeh(handle));

                civVehs[index] = new CivilianVeh(p.Identifiers["ip"]);
            }
        }
        public static void ToggleWarrant(string handle)
        {
            Player p = GetPlayerByHandle(handle);

            if (GetCivilian(handle) != null)
            {
                int index = civs.IndexOf(GetCivilian(handle));
                civs[index].WarrantStatus = !civs[index].WarrantStatus;
                SendMessage(p, "DispatchSystem", new[] { 0, 0, 0 }, $"Warrant status set to {civs[index].WarrantStatus.ToString()}");
            }
            else
                SendMessage(p, "DispatchSystem", new[] { 0, 0, 0 }, "You must set your name before you can toggle your warrant");
        }
        public static void SetCitations(string handle, int count)
        {
            Player p = GetPlayerByHandle(handle);

            if (GetCivilian(handle) != null)
            {
                int index = civs.IndexOf(GetCivilian(handle));
                civs[index].CitationCount = count;

                SendMessage(p, "DispatchSystem", new[] { 0, 0, 0 }, $"Citation count set to {count.ToString()}");
            }
            else
                SendMessage(p, "DispatchSystem", new[] { 0, 0, 0 }, "You must set your name before you can set your citations");
        }
        public static async void InitializeEmergency(string handle)
        {
            Player p = GetPlayerByHandle(handle);

            if (GetEmergencyCall(handle) != null)
            {
                SendMessage(p, "DispatchSystem", new[] { 0, 0, 0 }, "You already have a 911 call!");
                return;
            }

            if (GetCivilian(handle) != null)
            {
                Civilian civ = GetCivilian(handle);

                if (server.ConnectedDispatchers.Length == 0)
                {
                    SendMessage(p, "DispatchSystem", new[] { 0, 0, 0 }, "It seems like there is no connected dispatchers at this moment!");
                    return;
                }

                EmergencyCall call;
                currentCalls.Add(call = new EmergencyCall(p.Identifiers["ip"], $"{civ.First} {civ.Last}"));
                SendMessage(p, "Dispatch911", new[] { 255, 0, 0 }, "Please wait for a dispatcher to respond");
                foreach (var peer in server.ConnectedDispatchers)
                {
                    await peer.TriggerNetEvent("911alert", civ, call);
                }
            }
            else
                SendMessage(p, "DispatchSystem", new[] { 0, 0, 0 }, "You must set your name before start a 911 call");
        }
        public static async void MessageEmergency(string handle, string msg)
        {
            Player p = GetPlayerByHandle(handle);

            EmergencyCall call = GetEmergencyCall(handle);
            if (call?.Accepted ?? true)
            {
                SendMessage(p, "DispatchSystem", new[] { 0, 0, 0 }, "You're call must be answered or started first");
                return;
            }

            string dispatcherIp = server.Calls[call.Id];
            ConnectedPeer peer = server.ConnectedDispatchers.First(x => x.RemoteIP == dispatcherIp);
            await peer.TryTriggerNetEvent(call.Id.ToString(), msg);
        }
        public static void EndEmergency(string handle)
        {
            Player p = GetPlayerByHandle(handle);

            EmergencyCall call = GetEmergencyCall(handle);
            if (call == null)
            {
                SendMessage(p, "DispatchSystem", new[] { 0, 0, 0 }, "There is no 911 call to end!");
                return;
            }

#if DEBUG
            SendMessage(p, "", new[] { 0, 0, 0 }, "Ending 911 call...");
#endif

            string dispatcherIp = server.Calls[call.Id];
            ConnectedPeer peer = server.ConnectedDispatchers.First(x => x.RemoteIP == dispatcherIp);
#pragma warning disable 4014
            peer.TryTriggerNetEvent("end" + call.Id);
#pragma warning restore 4014

            currentCalls.Remove(call);
            SendMessage(p, "Dispatch911", new[] { 255, 0, 0 }, "Ended the 911 call");
        }
        #endregion

        #region Vehicle Events
        public static void SetVehicle(string handle, string plate)
        {
            Player p = GetPlayerByHandle(handle);

            if (GetCivilian(handle) == null)
            {
                SendMessage(p, "DispatchSystem", new[] { 0, 0, 0 }, "You must set your name before you can set your vehicle");
                return;
            }

            if (GetCivilianVehByPlate(plate) != null && GetPlayerByIp(GetCivilianVeh(handle).SourceIP) != p)
            {
                SendMessage(p, "DispatchSystem", new[] { 0, 0, 0 }, $"That vehicle already exists in the system!");
                return;
            }

            if (GetCivilianVeh(handle) != null)
            {
                Int32 index = civVehs.IndexOf(GetCivilianVeh(handle));

                civVehs[index] = new CivilianVeh(p.Identifiers["ip"]) { Plate = plate, Owner = GetCivilian(handle) };

                SendMessage(p, "DispatchSystem", new[] { 0, 0, 0 }, $"New vehicle set to {civVehs[index].Plate}");
            }
            else
            {
                civVehs.Add(new CivilianVeh(p.Identifiers["ip"]) { Plate = plate, Owner = GetCivilian(handle) });

                Int32 index = civVehs.IndexOf(GetCivilianVeh(handle));
                SendMessage(p, "DispatchSystem", new[] { 0, 0, 0 }, $"New vehicle set to {civVehs[index].Plate}");
            }
        }
        public static void ToggleVehicleStolen(string handle)
        {
            Player p = GetPlayerByHandle(handle);

            if (GetCivilian(handle) == null)
            {
                SendMessage(p, "DispatchSystem", new[] { 0, 0, 0 }, "You must set your name before you can set your vehicle stolen");
                return;
            }

            if (GetCivilianVeh(handle) != null)
            {
                int index = civVehs.IndexOf(GetCivilianVeh(handle));
                civVehs[index].StolenStatus = !civVehs[index].StolenStatus;

                if (civVehs[index].StolenStatus)
                {
                    Civilian civ = Civilian.CreateRandomCivilian();
                    civVehs[index].Owner = civ;
                    civs.Add(civ);
                }
                else
                {
                    Civilian civ = civVehs[index].Owner;
                    civs.Remove(civ);
                    civVehs[index].Owner = GetCivilian(handle);
                }


                SendMessage(p, "DispatchSystem", new[] { 0, 0, 0 }, $"Stolen status set to {civVehs[index].StolenStatus.ToString()}");
            }
            else
                SendMessage(p, "DispatchSystem", new[] { 0, 0, 0 }, "You must set your vehicle before you can set your vehicle stolen");
        }
        public static void ToggleVehicleRegistration(string handle)
        {
            Player p = GetPlayerByHandle(handle);

            if (GetCivilian(handle) == null)
            {
                SendMessage(p, "DispatchSystem", new[] { 0, 0, 0 }, "You must set your name before you can set your vehicle registration");
                return;
            }

            if (GetCivilianVeh(handle) != null)
            {
                int index = civVehs.IndexOf(GetCivilianVeh(handle));
                civVehs[index].Registered = !civVehs[index].Registered;

                SendMessage(p, "DispatchSystem", new[] { 0, 0, 0 }, $"Registration status set to {civVehs[index].Registered.ToString()}");
            }
            else
                SendMessage(p, "DispatchSystem", new[] { 0, 0, 0 }, "You must set your vehicle before you can set your Regisration");
        }
        public static void ToggleVehicleInsurance(string handle)
        {
            Player p = GetPlayerByHandle(handle);

            if (GetCivilian(handle) == null)
            {
                SendMessage(p, "DispatchSystem", new[] { 0, 0, 0 }, "You must set your name before you can set your vehicle insurance");
                return;
            }

            if (GetCivilianVeh(handle) != null)
            {
                int index = civVehs.IndexOf(GetCivilianVeh(handle));
                CivilianVeh last = civVehs[index];

                civVehs[index].Insured = !civVehs[index].Insured;

                SendMessage(p, "DispatchSystem", new[] { 0, 0, 0 }, $"Insurance status set to {civVehs[index].Insured.ToString()}");
            }
            else
                SendMessage(p, "DispatchSystem", new[] { 0, 0, 0 }, "You must set your vehicle before you can set your Insurance");
        }
        #endregion

        #region Police Events
        public static void AddOfficer(string handle, string callsign)
        {
            Player p = GetPlayerByHandle(handle);

            if (GetCivilian(handle) != null)
            {
                SendMessage(p, "DispatchSystem", new[] { 0, 0, 0 },
                    "You cannot be a officer and a civilian at the same time.");
                return;
            }

            if (GetOfficer(handle) == null)
            {
                officers.Add(new Officer(p.Identifiers["ip"], callsign));
                SendMessage(p, "DispatchSystem", new[] { 0, 0, 0 }, $"Assigning new officer for callsign {callsign}");
#if DEBUG
                SendMessage(p, "", new[] { 0, 0, 0 }, "Creating new Officer profile...");
#endif
            }
            else
            {
                int index = officers.IndexOf(GetOfficer(handle));
                officers[index] = new Officer(p.Identifiers["ip"], callsign);
                SendMessage(p, "DispatchSystem", new[] { 0, 0, 0 }, $"Changing your callsign to {callsign}");
            }
        }
        public static void DisplayStatus(string handle)
        {
            Player p = GetPlayerByHandle(handle);

            if (GetOfficer(handle) != null)
            {
                Officer ofc = GetOfficer(handle);

                SendMessage(p, "DispatchSystem", new[] { 0, 0, 0 }, string.Format("Your status is: {0}", ofc.Status == OfficerStatus.OffDuty ? "Off Duty" : ofc.Status == OfficerStatus.OnDuty ? "On Duty" : "Busy"));
            }
            else
                SendMessage(p, "DispatchSystem", new[] { 0, 0, 0 }, "You must create an officer first");
        }
        public static void ToggleOnDuty(string handle)
        {
            Player p = GetPlayerByHandle(handle);

            if (GetOfficer(handle) != null)
            {
                Officer ofc = GetOfficer(handle);

                if (ofc.Status == OfficerStatus.OnDuty)
                {
                    SendMessage(p, "DispatchSystem", new[] { 0, 0, 0 }, "You are already on duty dummy!");
                    return;
                }

                ofc.Status = OfficerStatus.OnDuty;
                SendMessage(p, "DispatchSystem", new[] { 0, 0, 0 }, "New officer status set to On Duty");
            }
            else
                SendMessage(p, "DispatchSystem", new[] { 0, 0, 0 }, "You must create an officer first");
        }
        public static void ToggleOffDuty(string handle)
        {
            Player p = GetPlayerByHandle(handle);

            if (GetOfficer(handle) != null)
            {
                Officer ofc = GetOfficer(handle);

                if (ofc.Status == OfficerStatus.OffDuty)
                {
                    SendMessage(p, "DispatchSystem", new[] { 0, 0, 0 }, "You are already off duty dummy!");
                    return;
                }

                ofc.Status = OfficerStatus.OffDuty;
                SendMessage(p, "DispatchSystem", new[] { 0, 0, 0 }, "New officer status set to Off Duty");
            }
            else
                SendMessage(p, "DispatchSystem", new[] { 0, 0, 0 }, "You must create an officer first");
        }
        public static void ToggleBusy(string handle)
        {
            Player p = GetPlayerByHandle(handle);

            if (GetOfficer(handle) != null)
            {
                Officer ofc = GetOfficer(handle);

                if (ofc.Status == OfficerStatus.Busy)
                {
                    SendMessage(p, "DispatchSystem", new[] { 0, 0, 0 }, "You are already busy dummy!");
                    return;
                }

                ofc.Status = OfficerStatus.Busy;
                SendMessage(p, "DispatchSystem", new[] { 0, 0, 0 }, "New officer status set to Busy");
            }
            else
                SendMessage(p, "DispatchSystem", new[] { 0, 0, 0 }, "You must create an officer first");
        }
        public static void RequestCivilian(string handle, string first, string last)
        {
            Player invoker = GetPlayerByHandle(handle);
            Civilian civ = GetCivilianByName(first, last);

            if (civ != null)
            {
                SendMessage(invoker, "DispatchSystem", new[] { 0, 0, 0 }, "Results: ");
                SendMessage(invoker, "DispatchSystem", new[] { 0, 0, 0 }, $"First: {civ.First} | Last: {civ.Last}");
                SendMessage(invoker, "DispatchSystem", new[] { 0, 0, 0 }, $"Warrant: {civ.WarrantStatus.ToString()}");
                SendMessage(invoker, "DispatchSystem", new[] { 0, 0, 0 }, $"Citations: {civ.CitationCount.ToString()}");
            }
            else
                SendMessage(invoker, "DispatchSystem", new[] { 0, 0, 0 }, "That civilian doesn't exist in the system");
        }
        public static void RequestCivilianVeh(string handle, string plate)
        {
            Player invoker = GetPlayerByHandle(handle);
            CivilianVeh civVeh = GetCivilianVehByPlate(plate);

            if (civVeh != null)
            {
                SendMessage(invoker, "DispatchSystem", new[] { 0, 0, 0 }, "Results: ");
                SendMessage(invoker, "DispatchSystem", new[] { 0, 0, 0 }, $"Plate: {civVeh.Plate.ToUpper()}");
                SendMessage(invoker, "DispatchSystem", new[] { 0, 0, 0 }, $"Stolen: {civVeh.StolenStatus.ToString()}");
                SendMessage(invoker, "DispatchSystem", new[] { 0, 0, 0 }, $"Registered: {civVeh.Registered.ToString()}");
                SendMessage(invoker, "DispatchSystem", new[] { 0, 0, 0 }, $"Insured: {civVeh.Insured.ToString()}");
                if (civVeh.Registered) SendMessage(invoker, "DispatchSystem", new[] { 0, 0, 0 }, $"R/O: {civVeh.Owner.First} {civVeh.Owner.Last}");
            }
            else
                SendMessage(invoker, "DispatchSystem", new[] { 0, 0, 0 }, "That vehicle doesn't exist in the system");
        }
        public static void AddCivilianNote(string invokerHandle, string first, string last, string note)
        {
            Player invoker = GetPlayerByHandle(invokerHandle);
            Civilian civ = GetCivilianByName(first, last);

            if (civ != null)
            {
                int index = civs.IndexOf(civ);
                civs[index].Notes.Add(note);
                SendMessage(invoker, "DispatchSystem", new[] { 0, 0, 0 }, $"Note of \"{note}\" has been added to the Civilian");
            }
            else
                SendMessage(invoker, "DispatchSystem", new[] { 0, 0, 0 }, "That name doesn't exist in the system");
        }
        public static void TicketCivilian(string invokerHandle, string first, string last, string reason, float amount)
        {
            Player invoker = GetPlayerByHandle(invokerHandle);
            Civilian civ = GetCivilianByName(first, last);

            if (civ != null)
            {
                int index = civs.IndexOf(civ);
                Player p = GetPlayerByIp(civs[index].SourceIP);
                civs[index].CitationCount++;
                civs[index].Tickets.Add(new Ticket(reason, amount));
                if (p != null)
                    SendMessage(p, "Ticket", new[] { 255, 0, 0 }, $"{invoker.Name} tickets you for ${amount.ToString()} because of {reason}");
                SendMessage(invoker, "DispatchSystem", new[] { 0, 0, 0 }, $"You successfully ticketed {p.Name} for ${amount.ToString()}");
            }
            else
                SendMessage(invoker, "DispatchSystem", new[] { 0, 0, 0 }, "That name doesn't exist in the system");
        }
        public static void DisplayCivilianTickets(string invokerHandle, string first, string last)
        {
            Player invoker = GetPlayerByHandle(invokerHandle);
            Civilian civ = GetCivilianByName(first, last);

            if (civ != null)
            {
                int index = civs.IndexOf(civ);
                if (civs[index].Tickets.Count() == 0)
                    SendMessage(invoker, "", new[] { 0, 0, 0 }, "^7None");
                else
                    civs[index].Tickets.ForEach(x => SendMessage(invoker, "", new[] { 0, 0, 0 }, $"^7${x.Amount}: {x.Reason}"));
            }
            else
                SendMessage(invoker, "DispatchSystem", new[] { 0, 0, 0 }, "That name doesn't exist in the system");
        }
        public static void DipslayCivilianNotes(string handle, string first, string last)
        {
            Player invoker = GetPlayerByHandle(handle);
            Civilian civ = GetCivilianByName(first, last);

            if (civ != null)
            {
                if (civ.Notes.Count == 0)
                    SendMessage(invoker, "", new[] { 0, 0, 0 }, "^7None");
                else
                    civ.Notes.ForEach(x => SendMessage(invoker, "", new[] { 0, 0, 0 }, x));
            }
            else
                SendMessage(invoker, "DispatchSystem", new[] { 0, 0, 0 }, "That name doesn't exist in the system");
        }
        public static void AddBolo(string handle, string reason)
        {
            Player p = GetPlayerByHandle(handle);
            bolos.Add(new Bolo(p.Name, p.Identifiers["ip"], reason));
            SendMessage(p, "DispatchSystem", new[] { 0, 0, 0 }, $"BOLO for \"{reason}\" added");
        }
        public static void ViewBolos(string handle)
        {
            Player p = GetPlayerByHandle(handle);
            if (bolos.Count > 0)
                bolos.ToList().ForEach(x => SendMessage(p, "", new[] { 0, 0, 0 }, $"^8{x.Player}^7: ^3{x.Reason}"));
            else
                SendMessage(p, "", new[] { 0, 0, 0 }, "^7None");
        }
        #endregion
        #endregion

        private void OnChatMessage(int source, string n, string msg)
        {
            Player p = this.Players[source];
            var args = msg.Split(' ').ToList();
            var cmd = args[0].ToLower();
            args.RemoveAt(0);

            // Reflection
            Commands instance = new Commands();
            var command = typeof(Commands).GetMethods().Where(x => x.GetCustomAttributes<CommandAttribute>().Any())
                .FirstOrDefault(x => string.Equals(x.GetCustomAttributes<CommandAttribute>().ToArray()[0].Command, cmd, StringComparison.CurrentCultureIgnoreCase));
            if (command == null) return;

            CancelEvent();
            CommandAttribute information = command.GetCustomAttributes<CommandAttribute>().ToArray()[0];
            switch (information.Type)
            {
                case CommandType.Civilian:
                    switch (perms.CivilianPermission)
                    {
                        case Permission.Everyone:
                            command.Invoke(instance, new object[] {p, args.ToArray()});
                            break;
                        case Permission.Specific:
                            if (perms.CivContains(IPAddress.Parse(p.Identifiers["ip"])))
                                command.Invoke(instance, new object[] { p, args.ToArray() });
                            else
                                SendMessage(p, "DispatchSystem", new[] { 0, 0, 0 }, "You don't have the permission to do that!");
                            break;
                        case Permission.None:
                            break;
                        default:
                            throw new ArgumentOutOfRangeException();
                    }
                    break;
                case CommandType.Leo:
                    switch (perms.LeoPermission)
                    {
                        case Permission.Everyone:
                            command.Invoke(instance, new object[] { p, args.ToArray() });
                            break;
                        case Permission.Specific:
                            if (perms.LeoContains(IPAddress.Parse(p.Identifiers["ip"])))
                                command.Invoke(instance, new object[] { p, args.ToArray() });
                            else
                                SendMessage(p, "DispatchSystem", new[] { 0, 0, 0 }, "You don't have the permission to do that!");
                            break;
                        case Permission.None:
                            break;
                        default:
                            throw new ArgumentOutOfRangeException();
                    }
                    break;
                default:
                    throw new ArgumentOutOfRangeException();
            }
        }
    }
}
