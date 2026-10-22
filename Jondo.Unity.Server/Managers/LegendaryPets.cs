using System;
using System.Collections.Generic;
using System.Linq;

namespace Jondo.Unity.Server.Managers
{
    /// <summary>
    /// The legendary pets: a worn pet given the legendary status, and then its power -- a spell it
    /// lends its master in a fight.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The client's data says it all. The one item whose effects give 2862 «Estatus legendario» is
    /// «Reflejo de bebemot» (20968); the 32 «Croqueta impregnada de ...» give 2864 «Da el hechizo»
    /// with a legendary power's category (legendarypowerscategoriesdataroot: 30 «Carapace du
    /// Scarafeuille», its spell 29819) and 1175 with the spell and its grade. Both are pet food
    /// (type 209), and the client feeds them the way it equips: dropped on the worn pet, the
    /// confirmation (EquipmentBehavior.ConfirmFeedLegendary, «ui.evolutive.pet.confirmFeedLegendary»)
    /// moves one onto the pet's slot (iuk, quantity 1).
    /// </para>
    /// <para>
    /// The categories are all «overridable»: a new power replaces the one the pet had. The power is
    /// the 1175's spell, offered in the fight's bar while the pet is worn (FightSpellLayout).
    /// </para>
    /// </remarks>
    public static class LegendaryPets
    {
        /// <summary>«Estatus legendario».</summary>
        public const int LegendaryStatus = 2862;

        /// <summary>«Da el hechizo: #3», a legendary power's category.</summary>
        public const int PowerCategory = 2864;

        /// <summary>The power's spell (dice number) and grade (dice side).</summary>
        public const int PowerSpell = 1175;

        /// <summary>The worn pet's slot, the pet's or the mount's.</summary>
        public const int PetSlot = 8;

        /// <summary>The pets that may become legendary: «Mascota» (18) and «Mascotura» (121).</summary>
        private static readonly HashSet<int> PetTypes = new() { 18, 121 };

        /// <summary>What a food does to a legendary pet, read off its template; null for any other item.</summary>
        public sealed record Food(bool GivesStatus, int Category, int Spell, int Grade);

        private static readonly Dictionary<int, Food?> _foods = new();
        private static readonly object _gate = new();

        public static Food? FoodOf(int template)
        {
            lock (_gate)
            {
                if (_foods.TryGetValue(template, out var known)) return known;
            }
            Food? food = null;
            try
            {
                var effects = DatabaseManager.GetItemEffectsData(DatabaseManager.GetItemTemplatePossibleEffects(template));
                bool status = effects.Any(e => e.EffectId == LegendaryStatus);
                var category = effects.FirstOrDefault(e => e.EffectId == PowerCategory);
                var spell = effects.FirstOrDefault(e => e.EffectId == PowerSpell);
                if (status) food = new Food(true, 0, 0, 0);
                else if (category != null && spell != null && spell.DiceNum > 0)
                    food = new Food(false, category.Value, spell.DiceNum, Math.Max(1, spell.DiceSide));
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[Legendary pets] Could not read template {template}: {ex.Message}");
            }
            lock (_gate) _foods[template] = food;
            return food;
        }

        public static bool IsPet(int template) => PetTypes.Contains(Mounts.TypeOf(template));

        public static bool IsLegendary(Equipment.Item pet) => pet.Effects.Any(e => e.Effect == LegendaryStatus);

        /// <summary>Why this food cannot go into that pet, or null when it can.</summary>
        public static string? WhyNotFeed(Equipment.Item? pet, Food food)
        {
            if (pet == null || !IsPet(pet.Template)) return "no pet worn";
            if (food.GivesStatus)
            {
                if (IsLegendary(pet)) return "the pet is legendary already";
                // «Pour faire évoluer vos familiers ... ils doivent être de niveau 100» (Pets).
                return Pets.IsPet(pet.Template) && Pets.LevelOf(pet.Effects) < Pets.TopLevel
                    ? $"the pet is not at level {Pets.TopLevel}" : null;
            }
            return IsLegendary(pet) ? null : "the pet is not legendary";
        }

        /// <summary>
        /// The pet as the food leaves it: legendary, or with this power instead of the one it had.
        /// </summary>
        public static List<Equipment.ItemEffect> Fed(IEnumerable<Equipment.ItemEffect> effects, Food food)
        {
            var fed = effects.ToList();
            if (food.GivesStatus)
            {
                fed.Add(new Equipment.ItemEffect(LegendaryStatus, 0, 0, 0));
                // A growing pet goes past its last level: the type's top, 101.
                if (fed.Any(e => e.Effect == Pets.Level)) fed = Rides.With(fed, Pets.Level, Pets.MaxLevel);
                return fed;
            }
            fed.RemoveAll(e => e.Effect is PowerCategory or PowerSpell);
            fed.Add(new Equipment.ItemEffect(PowerCategory, food.Category, 0, 0));
            fed.Add(new Equipment.ItemEffect(PowerSpell, 0, food.Spell, food.Grade));
            return fed;
        }

        /// <summary>The power a pet lends, if it is legendary and has one: (spell, grade), or (0, 0).</summary>
        public static (int Spell, int Grade) PowerOf(Equipment.Item? pet)
        {
            if (pet == null || !IsLegendary(pet)) return (0, 0);
            var power = pet.Effects.FirstOrDefault(e => e.Effect == PowerSpell);
            return power.Effect == PowerSpell && power.DiceNum > 0 ? ((int)power.DiceNum, (int)Math.Max(1, power.DiceSide)) : (0, 0);
        }

        /// <summary>The power of the pet the character in session wears.</summary>
        public static (int Spell, int Grade) PowerOfWornPet() => PowerOf(Equipment.InSlot(PetSlot));
    }
}
