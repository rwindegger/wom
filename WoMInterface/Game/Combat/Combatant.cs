﻿using System.Collections.Generic;
using WoMInterface.Game.Model;
using WoMInterface.Game.Random;

namespace WoMInterface.Game.Combat
{
    public class Combatant
    {
        public bool IsHero { get; set; } = false;
        public Entity Entity { get; }
        public int InititativeValue { get; set; }
        public Dice Dice { get; set; }
        public List<Entity> Enemies { get; set; }

        public Combatant(Entity entity)
        {
            Entity = entity;
        }

    }
}
