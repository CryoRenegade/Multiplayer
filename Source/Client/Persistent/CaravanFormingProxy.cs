﻿using RimWorld;
using System;
using UnityEngine;
using Verse;

namespace Multiplayer.Client
{
    public class CaravanFormingProxy : Dialog_FormCaravan, ISwitchToMap
    {
        public static CaravanFormingProxy drawing;

        public CaravanFormingSession Session => map.MpComp().caravanForming;

        public CaravanFormingProxy(Map map, bool reform = false, Action onClosed = null, bool mapAboutToBeRemoved = false, IntVec3? meetingSpot = null) : base(map, reform, onClosed, mapAboutToBeRemoved, meetingSpot)
        {
        }

        public override void DoWindowContents(Rect inRect)
        {
            drawing = this;

            try
            {
                var session = Session;

                if (session == null)
                {
                    Close();
                }
                else if (session.uiDirty)
                {
                    Notify_TransferablesChanged();
                    startingTile = session.startingTile;
                    destinationTile = session.destinationTile;
                    autoSelectTravelSupplies = session.autoSelectTravelSupplies;
                    if (autoSelectTravelSupplies)
                        SelectApproximateBestTravelSupplies();

                    session.uiDirty = false;
                }

                base.DoWindowContents(inRect);
            }
            finally
            {
                drawing = null;
            }
        }
    }

}
