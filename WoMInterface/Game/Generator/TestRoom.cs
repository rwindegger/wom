﻿using WoMInterface.Game.Combat;
using WoMInterface.Game.Interaction;
using WoMInterface.Game.Model;

namespace WoMInterface.Game.Generator
{
    public class TestRoom : Adventure
    {
        private SimpleCombat simpleFight;

        public TestRoom(SimpleCombat simpleFight)
        {
            this.simpleFight = simpleFight;
        }

        public override void NextStep(Mogwai mogwai, Shift shift)
        {
            if (AdventureState == AdventureState.CREATION)
            {
                simpleFight.Create(mogwai, shift);
                AdventureState = AdventureState.RUNNING;
            }
            
            if (!simpleFight.Run())
            {
                AdventureState = AdventureState.FAILED;
                return;
            }

            AdventureState = AdventureState.COMPLETED;
        }
    }
}