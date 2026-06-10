# Veilwalkers
## AR Monster Hunting Game - Design Document

**Project Name:** Veilwalkers  
**Tagline:** "Monsters are closer than you think."  
**Genre:** Augmented Reality (AR) Monster Hunting / Collection  
**Platform:** Android (Google Play Store)  
**Target Rating:** Teen (with age-appropriate scary monsters)  
**Monetization:** Free-to-play with Monster Credits via In-App Purchases

---

## 1. Game Overview

Veilwalkers is an Augmented Reality game where players use their phone's camera to lure, observe, capture, and slay monsters that appear in the real world around them.

- Monsters are overlaid into the player's actual environment using AR (ARCore).
- Players spend **Monster Credits** to interact with monsters.
- New players start with **20 free credits**.
- After the free credits run out, players purchase more credit packs.

The game combines collection, exploration, and light combat/hunting mechanics in a spooky but stylized art style suitable for a Teen rating.

### Lore
There are exactly **67** monsters in the entire Veilwalkers universe.  
Don't ask why. Just accept it.

---

## 2. Core Gameplay Loop

1. Open the app and enter AR mode using the phone camera.
2. Spend Monster Credits to **Lure** a monster into the current view.
3. Interact with the monster in AR:
   - Scan it
   - Attempt to Capture it
   - Spend extra credits to **Slay** it for better rewards
4. Collect monsters in the Codex.
5. Repeat. Run out of credits → Go to the Shop.

Monsters appear anchored in real-world space (floors, grass, furniture, etc.) with proper occlusion and lighting.

---

## The Pit (Miniature Roman Colosseum)

**The Pit** is a special AR object that players can place in the real world. It appears as a **miniature Roman Colosseum** (about the size of a dinner plate or small table).

### How It Works
- After capturing monsters, players can **miniaturize** them.
- They can then drop these tiny versions into **The Pit**.
- The miniaturized monsters fight each other inside the colosseum in real-time AR.
- Fights can be:
  - Automatic (AI-controlled)
  - Or simple player-controlled (tap to attack/special moves)
- Winners earn rewards: XP, rare materials, or exclusive trophies.

### Integration with Credits
- Miniaturizing a monster costs **1–2 Credits**.
- Hosting a fight in The Pit can cost **1 Credit** (or be free with limits).
- Players can spend credits to:
  - Boost their monster’s stats temporarily during a fight
  - Revive a defeated miniature monster
  - Unlock special arena effects (fire, lightning, etc.)

### Why It’s Cool
- Gives captured monsters a second life and purpose.
- Creates a fun spectator / collection battle mode.
- Looks amazing in AR — tiny monsters fighting inside a detailed miniature colosseum on your coffee table or floor.
- Strong social feature potential (record fights and share them).

This adds a whole new layer of gameplay beyond just luring and capturing.

---

## 3. Monster Credit System

### Starting Economy
- **20 Free Credits** given on first launch.
- Daily login rewards or ad rewards can give small amounts of free credits.

### Ways to Spend Credits

| Action                    | Cost     | Description                                                                 | Best For                  |
|---------------------------|----------|-----------------------------------------------------------------------------|---------------------------|
| **Lure Basic Monster**    | 1        | Summon a normal monster into AR                                             | Core loop                 |
| **Premium Lure**          | 4        | Significantly higher chance of rare or high-level monsters                  | Collectors & completionists |
| **Slay Monster**          | 3        | Enter combat mode and defeat the monster for better rewards                 | Hunters / aggressive players |
| **Strong Capture**        | 2        | Increases success rate when trying to capture a monster                     | Collectors                |
| **Stability Boost**       | 1        | Makes the current monster easier to scan/capture/slay during the encounter  | All players               |
| **Multi-Lure**            | 5        | Summon two monsters at the same time                                        | Advanced players          |
| **Nightveil Filter**      | 2        | Applies atmospheric visual filter + small rarity boost                      | Atmosphere-focused players |
| **Retry Encounter**       | 1        | Retry a failed capture or slay attempt                                      | Reduces frustration       |

**Key Design Goal:**  
Credits should feel valuable. Basic luring is cheap, while **Premium Lures** and **Slaying** are the main reasons players spend money.

---

## 4. Slaying vs Capturing

When a monster appears, players have meaningful choices:

- **Scan** (Free) — Learn about the monster
- **Capture** (Free or 2 credits for better odds)
- **Slay** (3 credits) — Fight and defeat the monster

**Slaying rewards** (better than capturing):
- Rare crafting materials
- Higher XP
- Exclusive monster variants or trophies
- Better entries in the Codex

This creates two distinct player types:
- **Collectors** → Focus on capturing and completing the Codex
- **Hunters** → Focus on slaying for loot and progression

---

## 5. Visual Style & Tone

- **Art Style:** Stylized spooky / atmospheric (not hyper-realistic gore)
- **Color Palette:** Dark backgrounds with cyan/teal accents
- **Monster Design:** Creepy but age-appropriate (Teen rating friendly)
  - Examples: Tall antlered shadow figures, multi-eyed furry creatures, spider-like beasts
- **Overall Vibe:** Mysterious and slightly unsettling, like a modern "Gravity Falls" or "Stranger Things" tone

**Content Rating Target:** Teen  
Avoid excessive blood, gore, or intense horror imagery.

---

## 6. Key Screens & UI Mockups

### Generated Mockup Images

All images are saved in `/home/workdir/artifacts/imagine_images/`

**Core Screens:**

- `DBHY8.jpg` — Onboarding / Welcome screen (shows 20 free credits)
- `YJY0A.jpg` — Home screen with "Enter AR Hunt" button
- `5mbQP.jpg` — AR Camera view (indoor living room with tall antlered monster)
- `9bVA3.jpg` — AR Camera view (outdoor backyard with spider-like monster)
- `ku8KS.jpg` — Monster encounter screen (with actions)
- `u4wU8.jpg` — AR encounter with prominent **SLAY** button (red)
- `tKxkv.jpg` — Shop screen with credit packs
- `XeLpr.jpg` — Monster Codex / Collection screen
- `YPDSc.jpg` — Monster detail view
- `dI6NF.jpg` — "How to Use Credits" popup

---

## 7. Monetization Strategy

### In-App Purchases (Google Play Billing)

**Credit Packs (Recommended):**
- Starter Pack: 50 Credits – $1.99
- Hunter Pack: 150 Credits + 20 bonus – $4.99
- Veil Pack: 400 Credits + 100 bonus + Rare Lure – $9.99

**Design Principles:**
- Make the free experience feel generous at the start (20 free credits).
- Create natural spending moments (running out during a good session, wanting rare monsters, wanting to slay).
- Avoid aggressive paywalls. Players should still enjoy the game without spending.

---

## 8. Technical Considerations

- **AR Technology:** Google ARCore (via Unity AR Foundation recommended for faster development)
- **Permissions:** Camera (required), Location (optional but recommended for world spawns)
- **Safety Warning:** Must show AR safety warning immediately when entering AR mode (parental supervision + awareness of surroundings). This is a Google Play requirement.
- **Device Support:** AR Required or AR Optional (ARCore supported devices)
- **Performance:** Optimize for mid-range Android devices. Good plane detection and occlusion are critical for immersion.
- **Backend (Future):** Simple player progression, monster collection sync, and purchase verification.

**Recommended Tech Stack:**
- Unity 2022+ or newer
- AR Foundation + ARCore
- Google Play Billing Library
- Firebase or similar for backend (optional for MVP)

---

## 9. Google Play Policies & Content Rating

- **Content Rating:** Target **Teen**
  - Scary but stylized monsters are acceptable at Teen.
  - Avoid realistic gore or extreme horror.
- **AR Safety Warning:** Mandatory. Must appear immediately on AR launch.
- **Privacy:** Clear privacy policy required. Camera access must be properly disclosed.
- **Monetization:** Use official Google Play Billing for all credit purchases. No third-party payment systems.
- **Precedent:** Games like *Monster Hunter Now* (Niantic) prove this style of AR monster hunting + IAP is approved and successful.

---

## 10. Next Steps / Development Roadmap

### Phase 1: MVP (Minimum Viable Product)
- [ ] Basic AR camera with plane detection
- [ ] Simple monster spawning using credits
- [ ] 3–5 monster types with basic animations
- [ ] Capture + Codex system
- [ ] Shop with Google Play Billing
- [ ] Onboarding + safety warning

### Phase 2: Core Polish
- [ ] Slaying / combat system
- [ ] Better AR integration (occlusion, lighting, shadows)
- [ ] More monster variety and rarity tiers
- [ ] Daily rewards / free credit system
- [ ] Collection Codex with details

### Phase 3: Expansion
- [ ] Location-based spawns (GPS)
- [ ] Social features (sharing monsters)
- [ ] Events and limited-time monsters
- [ ] More credit sink options

---

## 11. Notes & Open Questions

- Should there be a light combat system for slaying, or keep it simple (tap to slay)?
- How rare should Premium Lures feel?
- Do we want a "bestiary" style Codex or more of a personal journal?
- Should monsters have levels that increase difficulty/rewards?

---

**Document Version:** 1.0  
**Last Updated:** June 10, 2026  
**Status:** Ready to begin development

---

*This document compiles the full game concept, mechanics, monetization, visual direction, and technical guidance for Veilwalkers.*