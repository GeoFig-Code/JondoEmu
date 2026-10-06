using Jondo.Unity.Server.Network;
using Xunit;

namespace Jondo.Unity.Tests.Combat
{
    /// <summary>
    /// The ivk payloads that answer remove (iul) and swap (iuv) on the spell bar.
    /// </summary>
    public class SpellBarEditTests
    {
        [Fact]
        public void A_cleared_slot_ivk_has_no_spell_and_names_the_spell_bar()
        {
            byte[] ivk = ConnectionProtocol.BuildShortcutCleared(7);
            var root = ProtoMessage.Parse(ivk);

            int bar = 0;
            byte[]? shortcut = null;
            foreach (var field in root.Fields)
            {
                if (field.FieldNumber == 2 && field.WireType == 2) shortcut = field.BytesValue;
                else if (field.FieldNumber == 3 && field.WireType == 0) bar = (int)field.VarIntValue;
            }

            Assert.Equal(ConnectionProtocol.SpellBar, bar);
            Assert.NotNull(shortcut);

            int slot = -1;
            bool hasSpell = false;
            foreach (var field in ProtoMessage.Parse(shortcut!).Fields)
            {
                if (field.FieldNumber == 2 && field.WireType == 0) slot = (int)field.VarIntValue;
                if (field.FieldNumber == 6) hasSpell = true;
            }

            Assert.Equal(7, slot);
            Assert.False(hasSpell);
        }

        [Fact]
        public void A_changed_slot_ivk_still_carries_the_spell()
        {
            byte[] ivk = ConnectionProtocol.BuildShortcutChanged(3, 12840);
            var root = ProtoMessage.Parse(ivk);

            int spell = 0;
            foreach (var field in root.Fields)
            {
                if (field.FieldNumber != 2 || field.WireType != 2) continue;
                foreach (var inner in ProtoMessage.Parse(field.BytesValue).Fields)
                {
                    if (inner.FieldNumber != 6 || inner.WireType != 2) continue;
                    foreach (var spellField in ProtoMessage.Parse(inner.BytesValue).Fields)
                    {
                        if (spellField.FieldNumber == 2 && spellField.WireType == 0)
                            spell = (int)spellField.VarIntValue;
                    }
                }
            }

            Assert.Equal(12840, spell);
        }
    }
}
