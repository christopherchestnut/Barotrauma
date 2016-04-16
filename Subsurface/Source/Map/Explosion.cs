﻿using Microsoft.Xna.Framework;
using Barotrauma.Lights;
using System;
using System.Collections.Generic;
using System.Xml.Linq;

namespace Barotrauma
{
    class Explosion
    {
        private Attack attack;
        
        private float force;

        private LightSource light;

        public float CameraShake;

        private bool sparks, shockwave, flames;

        public Explosion(XElement element)
        {
            attack = new Attack(element);

            force = ToolBox.GetAttributeFloat(element, "force", 0.0f);

            sparks      = ToolBox.GetAttributeBool(element, "sparks", true);
            shockwave   = ToolBox.GetAttributeBool(element, "shockwave", true);
            flames      = ToolBox.GetAttributeBool(element, "flames", true); 

            CameraShake = ToolBox.GetAttributeFloat(element, "camerashake", attack.Range*0.1f);
        }

        public void Explode(Vector2 worldPosition)
        {
            Hull hull = Hull.FindHull(worldPosition);

            if (shockwave)
            {
                GameMain.ParticleManager.CreateParticle("shockwave", worldPosition,
                    Vector2.Zero, 0.0f, hull);
            }

            for (int i = 0; i < attack.Range * 0.1f; i++)
            {
                Vector2 bubblePos = Rand.Vector(attack.Range * 0.5f);
                GameMain.ParticleManager.CreateParticle("bubbles", worldPosition+bubblePos,
                    bubblePos, 0.0f, hull);

                if (sparks)
                {
                    GameMain.ParticleManager.CreateParticle("spark", worldPosition,
                        Rand.Vector(Rand.Range(500.0f, 800.0f)), 0.0f, hull);
                }
                if (flames)
                {
                    GameMain.ParticleManager.CreateParticle("explosionfire", worldPosition + Rand.Vector(50f),
                        Rand.Vector(Rand.Range(50f, 100.0f)), 0.0f, hull);
                }
            }

            float displayRange = attack.Range;
            if (displayRange < 0.1f) return;

            light = new LightSource(worldPosition, displayRange, Color.LightYellow, hull != null ? hull.Submarine : null);
            CoroutineManager.StartCoroutine(DimLight());

            float cameraDist = Vector2.Distance(GameMain.GameScreen.Cam.Position, worldPosition)/2.0f;
            GameMain.GameScreen.Cam.Shake = CameraShake * Math.Max((displayRange - cameraDist) / displayRange, 0.0f);
            
            if (attack.GetStructureDamage(1.0f) > 0.0f)
            {
                RangedStructureDamage(worldPosition, displayRange, attack.GetStructureDamage(1.0f));
            }

            if (force == 0.0f && attack.Stun == 0.0f && attack.GetDamage(1.0f) == 0.0f) return;

            ApplyExplosionForces(worldPosition, attack.Range, force, attack.GetDamage(1.0f), attack.Stun);

            if (flames)
            {
                foreach (Item item in Item.ItemList)
                {
                    if (item.CurrentHull != hull || item.FireProof || item.Condition <= 0.0f) continue;
                    //if (item.ParentInventory != null) return;

                    if (Vector2.Distance(item.WorldPosition, worldPosition) > attack.Range * 0.1f) continue;

                    //item.Condition -= (float)Math.Sqrt(size.X) * deltaTime;

                    item.ApplyStatusEffects(ActionType.OnFire, 1.0f);
                }
            }

        }

        private IEnumerable<object> DimLight()
        {
            float currBrightness= 1.0f;
            float startRange = light.Range;

            while (light.Color.A > 0.0f)
            {
                light.Color = new Color(light.Color.R, light.Color.G, light.Color.B, currBrightness);
                light.Range = startRange * currBrightness;

                currBrightness -= 0.05f;

                yield return CoroutineStatus.Running;
            }

            light.Remove();
            
            yield return CoroutineStatus.Success;
        }

        public static void ApplyExplosionForces(Vector2 worldPosition, float range, float force, float damage = 0.0f, float stun = 0.0f)
        {
            if (range <= 0.0f) return;

            foreach (Character c in Character.CharacterList)
            {
                foreach (Limb limb in c.AnimController.Limbs)
                {
                    float dist = Vector2.Distance(limb.WorldPosition, worldPosition);

                    if (dist > range) continue;

                    float distFactor = 1.0f - dist / range;

                    if (limb.WorldPosition == worldPosition) continue;

                    c.AddDamage(limb.SimPosition, DamageType.None,
                        damage / c.AnimController.Limbs.Length * distFactor, 0.0f, stun * distFactor, false);
                    if (force > 0.0f)
                    {
                        limb.body.ApplyLinearImpulse(Vector2.Normalize(limb.WorldPosition - worldPosition) * distFactor * force);
                    }
                }
            }
        }

        public static void RangedStructureDamage(Vector2 worldPosition, float worldRange, float damage)
        {
            List<Structure> structureList = new List<Structure>();


            float dist = 600.0f;
            foreach (MapEntity entity in MapEntity.mapEntityList)
            {
                Structure structure = entity as Structure;
                if (structure == null) continue;

                if (structure.HasBody &&
                    !structure.IsPlatform &&
                    Vector2.Distance(structure.WorldPosition, worldPosition) < dist * 3.0f)
                {
                    structureList.Add(structure);
                }
            }

            foreach (Structure structure in structureList)
            {
                for (int i = 0; i < structure.SectionCount; i++)
                {
                    float distFactor = 1.0f - (Vector2.Distance(structure.SectionPosition(i, true), worldPosition) / worldRange);
                    if (distFactor > 0.0f) structure.AddDamage(i, damage * distFactor);
                }
            }
        }
    }
}
