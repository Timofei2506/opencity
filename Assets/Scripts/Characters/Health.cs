using UnityEngine;

namespace OpenCity
{
    /// <summary>Hit points + armour for every damageable actor (player, pedestrians, police, cars).</summary>
    public class Health : MonoBehaviour
    {
        public float MaxHP = 100f;
        public float HP = 100f;
        public float MaxArmor = 100f;
        public float Armor;

        public bool IsPlayer;
        public bool BloodFx = true;
        public bool IsDead { get; private set; }

        /// <summary>Invoked once when HP reaches zero. The owner decides what a corpse looks like.</summary>
        public System.Action<Health> Died;
        /// <summary>Invoked on every hit: (damage, worldPoint, direction).</summary>
        public System.Action<float, Vector3, Vector3> Hit;

        public float Fraction => MaxHP <= 0f ? 0f : Mathf.Clamp01(HP / MaxHP);

        void Awake()
        {
            if (HP <= 0f) HP = MaxHP;
        }

        public void SetMax(float max, bool refill = true)
        {
            MaxHP = Mathf.Max(1f, max);
            if (refill) HP = MaxHP;
        }

        public void SetArmor(float value)
        {
            Armor = Mathf.Clamp(value, 0f, MaxArmor);
        }

        public void Heal(float amount)
        {
            if (IsDead) return;
            HP = Mathf.Min(MaxHP, HP + Mathf.Abs(amount));
        }

        public void Revive(float hp)
        {
            IsDead = false;
            HP = Mathf.Clamp(hp, 1f, MaxHP);
        }

        /// <summary>
        /// Applies damage. Armour absorbs 65% of the hit until it is depleted.
        /// Returns the damage that actually went through.
        /// </summary>
        public float Damage(float amount, Vector3 point, Vector3 direction, GameObject source = null, bool headshot = false)
        {
            if (IsDead || amount <= 0f) return 0f;

            if (Armor > 0f)
            {
                float absorbed = Mathf.Min(Armor, amount * 0.65f);
                Armor -= absorbed;
                amount -= absorbed;
            }

            HP -= amount;
            if (Hit != null) Hit(amount, point, direction);
            if (BloodFx && FxFactory.Ready) FxFactory.Blood(point, direction, headshot ? 1.4f : 0.8f);

            if (IsPlayer && GameManager.I != null) GameManager.I.AddWanted(0.08f);

            if (HP <= 0f)
            {
                HP = 0f;
                IsDead = true;
                if (Died != null) Died(this);
            }
            return amount;
        }

        public void Kill(GameObject source = null)
        {
            Damage(HP + 1f, transform.position + Vector3.up, Vector3.up, source);
        }
    }
}
