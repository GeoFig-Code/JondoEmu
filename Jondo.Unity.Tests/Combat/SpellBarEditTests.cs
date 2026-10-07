using Jondo.Unity.Server.Network;
using Xunit;

namespace Jondo.Unity.Tests.Combat
{
    /// <summary>
    /// Payloads that answer remove (iul → ivr) and swap (iuv → ivk / ivr) on the spell bar.
    /// </summary>
    public class SpellBarEditTests
    {
        [Fact]
        public void A_removed_slot_ivr_is_flat_bar_then_slot()
        {
            byte[] ivr = ConnectionProtocol.BuildShortcutRemoved(7);
            var root = ProtoMessage.Parse(ivr);

            int bar = -1, slot = -1;
            foreach (var field in root.Fields)
            {
                if (field.FieldNumber == 1 && field.WireType == 0) bar = (int)field.VarIntValue;
                else if (field.FieldNumber == 2 && field.WireType == 0) slot = (int)field.VarIntValue;
            }

            Assert.Equal(ConnectionProtocol.SpellBar, bar);
            Assert.Equal(7, slot);
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

        [Fact]
        public void Iul_payload_is_bar_then_slot_like_the_captures()
        {
            // Traffic: iul { f1: 1, f2: 6 } — bar first, slot second.
            byte[] body = Pb.New().Var(1, ConnectionProtocol.SpellBar).Var(2, 6).Build();
            int bar = 0, slot = 0;
            foreach (var field in ProtoMessage.Parse(body).Fields)
            {
                if (field.FieldNumber == 1 && field.WireType == 0) bar = (int)field.VarIntValue;
                else if (field.FieldNumber == 2 && field.WireType == 0) slot = (int)field.VarIntValue;
            }

            Assert.Equal(ConnectionProtocol.SpellBar, bar);
            Assert.Equal(6, slot);
        }
    }
}
