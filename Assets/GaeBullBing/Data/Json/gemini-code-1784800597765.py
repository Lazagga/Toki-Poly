import random
import tkinter as tk
from tkinter import ttk, scrolledtext, messagebox
import csv
import os
import json

# ==========================================
# 1. JSON 파일 동적 로드 및 데이터 파싱
# ==========================================

def normalize_element(elem_str):
    if not elem_str:
        return "None"
    elem = elem_str.strip().capitalize()
    if elem == "Physics":
        return "Physics"
    elif elem == "Electric":
        return "Electric"
    return elem

def load_game_data():
    base_path = os.getcwd()
    
    # 1. Monster.json 파싱
    monster_file = os.path.join(base_path, "Monster.json")
    monster_base_stats = {}
    if os.path.exists(monster_file):
        with open(monster_file, "r", encoding="utf-8") as f:
            m_data = json.load(f)
            for m in m_data.get("monster_database", []):
                monster_base_stats[m["id"]] = {
                    "name": m["name"],
                    "hp": m["base_stats"]["max_hp"],
                    "speed": m["base_stats"]["move_speed"],
                    "def": m["base_stats"]["base_defense"],
                    "is_boss": (m["tier"] == "Boss")
                }

    # 2. Pattern.json 파싱
    pattern_file = os.path.join(base_path, "Pattern.json")
    wave_patterns = {}
    default_hp_mult = 1.35
    default_def_add = 20.0
    if os.path.exists(pattern_file):
        with open(pattern_file, "r", encoding="utf-8") as f:
            p_data = json.load(f)
            if p_data.get("wavedata"):
                w_info = p_data["wavedata"][0]
                default_hp_mult = w_info.get("multiplier", 1.35)
                default_def_add = w_info.get("defense_per_wave", 20.0)
            for wp in p_data.get("wave_patterns", []):
                wave_patterns[wp["level"]] = wp["spawn_pattern"]

    # 3. Tile.json 파싱
    tile_file = os.path.join(base_path, "Tile.json")
    tiles = []
    if os.path.exists(tile_file):
        with open(tile_file, "r", encoding="utf-8") as f:
            t_data = json.load(f)
            tile_defs = {td["id"]: td for td in t_data.get("tile_definitions", [])}
            for layout in t_data.get("board_layout", []):
                t_def = tile_defs.get(layout["tile_id"], {})
                tiles.append({
                    "index": layout["index"],
                    "type": t_def.get("type", "Normal"),
                    "element": normalize_element(t_def.get("element", "None")),
                    "build_tower_id": t_def.get("build_tower_id", "")
                })

    # 4. Tower.json 파싱
    tower_file = os.path.join(base_path, "Tower.json")
    tower_base_stats = {}
    if os.path.exists(tower_file):
        with open(tower_file, "r", encoding="utf-8") as f:
            tw_data = json.load(f)
            for tw in tw_data.get("tower_database", []):
                tower_base_stats[tw["id"]] = {
                    "name": tw["name"],
                    "element": normalize_element(tw["element"]),
                    "damage": tw["base_stats"]["damage"],
                    "range": tw["base_stats"]["range"],
                    "target_count": tw["base_stats"].get("target_count", 1),
                    "attack_count": tw["base_stats"].get("attack_count", 1)
                }

    # 5. Upgrade.json 파싱
    upgrade_file = os.path.join(base_path, "Upgrade.json")
    upgrades = {}
    if os.path.exists(upgrade_file):
        with open(upgrade_file, "r", encoding="utf-8") as f:
            u_data = json.load(f)
            for upg in u_data.get("tower_upgrade_database", []):
                elem = normalize_element(upg["element"])
                tier = upg["tier"]
                
                stat_mods = upg.get("stat_modifiers", [])
                
                effect_id = None
                if upg.get("effect_ids"):
                    effect_id = upg["effect_ids"][0]["id"]

                if elem not in upgrades:
                    upgrades[elem] = {}
                if tier not in upgrades[elem]:
                    upgrades[elem][tier] = []
                
                upgrades[elem][tier].append({
                    "stat_modifiers": stat_mods,
                    "effect": effect_id
                })

    return tiles, monster_base_stats, wave_patterns, tower_base_stats, upgrades, default_hp_mult, default_def_add

TILES, MONSTER_BASE_STATS, WAVE_PATTERNS, TOWER_BASE_STATS, UPGRADES, DEFAULT_HP_MULT, DEFAULT_DEF_ADD = load_game_data()

def get_circular_tile_distance(pos1, pos2):
    diff = abs(pos1 - pos2)
    return min(diff, 36 - diff)

# ==========================================
# 2. 게임 엔티티 클래스 정의
# ==========================================

class Monster:
    def __init__(self, mon_id, wave, bear_base_hp, hp_mult, def_add, boss_hp_override=None, boss_def_override=None):
        base = MONSTER_BASE_STATS.get(mon_id, {"name": "알수없음", "hp": 300, "speed": 2, "def": 0, "is_boss": False})
        self.id = mon_id
        self.name = base["name"]
        self.is_boss = base["is_boss"]
        
        scale = bear_base_hp / 420.0

        if self.is_boss:
            self.max_hp = boss_hp_override if boss_hp_override is not None else base["hp"]
            self.hp = self.max_hp
            self.defense = boss_def_override if boss_def_override is not None else base["def"]
        else:
            base_hp_scaled = base["hp"] * scale
            self.max_hp = base_hp_scaled * (hp_mult ** (wave - 1))
            self.hp = self.max_hp
            self.defense = base["def"] + (wave - 1) * def_add
            
        self.speed = base["speed"]
        self.position = 0
        self.distance_traveled = 0
        self.burn_stacks = 0
        self.is_frozen = False
        self.freeze_immunity_cd = 0
        self.is_shocked = False
        self.alive = True

    def add_burn_stacks(self, amount):
        self.burn_stacks += amount

    def take_damage(self, amount):
        if not self.alive:
            return
        def_factor = 100.0 / (100.0 + self.defense)
        final_dmg = amount * def_factor
        if self.is_shocked:
            final_dmg *= 1.3
            
        self.hp -= final_dmg
        if self.hp <= 0:
            self.hp = 0
            self.alive = False

class Tower:
    def __init__(self, tile_index, tower_id):
        base = TOWER_BASE_STATS.get(tower_id, {"name": "기본타워", "element": "Fire", "damage": 10, "range": 3, "target_count": 1, "attack_count": 1})
        self.tile_index = tile_index
        self.id = tower_id
        self.name = base["name"]
        self.element = base["element"]
        self.base_damage = base["damage"]
        self.range = base["range"]
        self.target_count = base["target_count"]
        self.attack_count = base["attack_count"]
        self.tier = 1
        
        self.add_damage = 0
        self.add_mult = 0.0
        self.effects = []
        
        self.can_attack = True
        self.cooldown_timer = 0
        self.cooldown_max = 0
        self.is_wall = False
        self.is_disabled_by_feather = False

    def apply_upgrade(self, upgrade_dict):
        self.tier += 1
        for mod in upgrade_dict.get("stat_modifiers", []):
            stat = mod["stat"]
            op = mod["operation"].lower()
            val = mod["value"]
            
            if stat == "damage":
                if op == "add":
                    self.add_damage += val
                elif op == "multiply":
                    self.add_mult += (val - 1.0)
            elif stat == "attack_count":
                if op == "add":
                    self.attack_count += val
                elif op == "multiply":
                    self.attack_count *= val
            elif stat == "target_count":
                if op == "add":
                    self.target_count += val
                elif op == "multiply":
                    self.target_count *= val
            elif stat == "range":
                if op == "set":
                    self.range = val
                elif op == "add":
                    self.range += val

        effect_id = upgrade_dict.get("effect")
        if effect_id:
            self.effects.append(effect_id)
            if effect_id == "cooldown":
                self.cooldown_max = 2
            elif effect_id == "line_tower_buff":
                self.can_attack = False
            elif effect_id == "wall":
                self.is_wall = True
                self.range = 0
                self.can_attack = False

    def get_final_damage(self, lap_count, global_element_buff, element_mult_buff, line_buff=0.0, target_is_frozen=False):
        lap_buff = lap_count * 0.05
        corner_buff = element_mult_buff.get(self.element, 0.0)
        freeze_add = 4.0 if (target_is_frozen and "freeze_damage_multiply" in self.effects) else 0.0
        
        total_mult = 1.0 + self.add_mult + lap_buff + corner_buff + line_buff + freeze_add
        element_extra = global_element_buff.get(self.element, 0)
        
        final_dmg = (self.base_damage + self.add_damage) * total_mult + element_extra
        return max(0.0, final_dmg)

# ==========================================
# 3. 단일 게임 시뮬레이션 클래스
# ==========================================

class GameSimulation:
    def __init__(self, bear_base_hp, hp_mult, def_add, boss_hp, boss_def):
        self.bear_base_hp = bear_base_hp
        self.hp_mult = hp_mult
        self.def_add = def_add
        self.boss_hp = boss_hp
        self.boss_def = boss_def
        
        self.player_life = 5
        self.player_pos = 0
        self.lap_count = 0
        self.current_wave = 1
        self.wave_kills = 0
        self.total_kills = 0
        self.total_turns = 0
        
        self.towers = {}
        self.monsters = []
        self.boss_monster = None
        self.global_element_buff = {"Fire": 0, "Ice": 0, "Physics": 0, "Electric": 0}
        self.element_mult_buff = {"Fire": 0.0, "Ice": 0.0, "Physics": 0.0, "Electric": 0.0}
        self.line_step_buff = [0.0, 0.0, 0.0, 0.0]
        self.tile_effects = {}
        
        self.pattern_index = 0
        self.game_over = False
        self.game_win = False
        self.failed_wave = None

    def roll_dice(self):
        return random.randint(1, 6) + random.randint(1, 6)

    def step_turn(self):
        if self.game_over or self.game_win:
            return

        self.total_turns += 1

        # 1. 주사위 & 이동 페이즈
        dice_sum = self.roll_dice()
        old_pos = self.player_pos
        
        traversed_tiles = [(old_pos + step) % 36 for step in range(1, dice_sum + 1)]
        self.player_pos = traversed_tiles[-1]
        
        if old_pos + dice_sum >= 36:
            self.lap_count += 1

        for tile_step in traversed_tiles:
            if tile_step in self.towers:
                t = self.towers[tile_step]
                if t.is_disabled_by_feather:
                    t.is_disabled_by_feather = False
                if "tile_step_line_buff" in t.effects:
                    line_idx = tile_step // 9
                    self.line_step_buff[line_idx] += 0.10

        if self.player_pos in (9, 27):
            element_sums = {"Fire": 0.0, "Ice": 0.0, "Physics": 0.0, "Electric": 0.0}
            for t in self.towers.values():
                element_sums[t.element] += t.get_final_damage(self.lap_count, self.global_element_buff, self.element_mult_buff)
            
            strongest_element = max(element_sums, key=element_sums.get)
            self.element_mult_buff[strongest_element] += 0.20

        # 2. 건설 및 업그레이드 페이즈
        tile_info = TILES[self.player_pos]
        if tile_info["type"] == "Normal" and tile_info["build_tower_id"]:
            if self.player_pos not in self.towers:
                self.towers[self.player_pos] = Tower(self.player_pos, tile_info["build_tower_id"])
            else:
                tower = self.towers[self.player_pos]
                if tower.tier < 3:
                    next_tier = tower.tier + 1
                    opts = UPGRADES.get(tower.element, {}).get(next_tier, [])
                    if opts:
                        chosen = random.choice(opts)
                        tower.apply_upgrade(chosen)
                else:
                    if self.global_element_buff.get(tower.element, 0) < 90:
                        self.global_element_buff[tower.element] += 30

        # 3. 몬스터 스폰 페이즈
        if self.current_wave <= 7:
            pattern = WAVE_PATTERNS.get(self.current_wave, ["MON_001", "MON_002"])
            mon_id = pattern[self.pattern_index % len(pattern)]
            self.pattern_index += 1
            new_mon = Monster(mon_id, self.current_wave, self.bear_base_hp, self.hp_mult, self.def_add)
            self.monsters.append(new_mon)
        elif self.current_wave == 8 and len([m for m in self.monsters if m.is_boss]) == 0:
            boss = Monster("BOSS_001", 8, self.bear_base_hp, self.hp_mult, self.def_add, self.boss_hp, self.boss_def)
            self.monsters.append(boss)
            self.boss_monster = boss
            self.drop_feather_on_strongest_tower()

        # 4. 몬스터 스탠바이 및 이동/타일 효과 페이즈
        for mon in self.monsters:
            if not mon.alive:
                continue

            if mon.burn_stacks > 0:
                standby_burn_dmg = mon.max_hp * 0.005 * mon.burn_stacks
                mon.take_damage(standby_burn_dmg)

            if not mon.alive:
                continue

            old_tile = mon.position
            
            if mon.freeze_immunity_cd > 0:
                mon.freeze_immunity_cd -= 1

            if mon.is_frozen:
                move_dist = 0
                mon.is_frozen = False
                mon.freeze_immunity_cd = 1
            else:
                move_dist = mon.speed

            if mon.is_boss:
                for step in range(1, move_dist + 1):
                    pass_pos = (old_tile + step) % 36
                    if pass_pos in (9, 18, 27):
                        self.drop_feather_on_strongest_tower()

                dest_pos = (old_tile + move_dist) % 36
                mon.position = dest_pos
                mon.distance_traveled += move_dist

                if dest_pos in self.towers and self.towers[dest_pos].is_wall:
                    wall_tower = self.towers[dest_pos]
                    wall_dmg = wall_tower.get_final_damage(self.lap_count, self.global_element_buff, self.element_mult_buff)
                    mon.take_damage(wall_dmg)

                if self.tile_effects.get(dest_pos, {}).get("fire", 0) > 0:
                    mon.add_burn_stacks(1)
                    entry_burn_dmg = mon.max_hp * 0.005 * mon.burn_stacks
                    mon.take_damage(entry_burn_dmg)

            else:
                for step in range(1, move_dist + 1):
                    curr_pos = (old_tile + step) % 36
                    mon.position = curr_pos
                    mon.distance_traveled += 1

                    if curr_pos in self.towers and self.towers[curr_pos].is_wall:
                        wall_tower = self.towers[curr_pos]
                        wall_dmg = wall_tower.get_final_damage(self.lap_count, self.global_element_buff, self.element_mult_buff)
                        mon.take_damage(wall_dmg)
                        break

                    if self.tile_effects.get(curr_pos, {}).get("fire", 0) > 0:
                        mon.add_burn_stacks(1)
                        entry_burn_dmg = mon.max_hp * 0.005 * mon.burn_stacks
                        mon.take_damage(entry_burn_dmg)

                    if not mon.alive or mon.distance_traveled >= 36:
                        break

            if mon.distance_traveled >= 36 and mon.alive:
                mon.alive = False
                if mon.is_boss:
                    self.player_life = 0
                else:
                    self.player_life -= 1

        # 5. 타워 공격 페이즈
        for tile_idx in range(36):
            if tile_idx in self.towers:
                tower = self.towers[tile_idx]
                
                if tower.is_disabled_by_feather or not tower.can_attack:
                    continue

                if tower.cooldown_timer > 0:
                    tower.cooldown_timer -= 1
                    continue
                
                candidates = [m for m in self.monsters if m.alive and get_circular_tile_distance(m.position, tower.tile_index) <= tower.range]
                if not candidates:
                    continue
                
                if "range_attack" in tower.effects:
                    primary_targets = candidates
                else:
                    candidates.sort(key=lambda m: (m.is_boss, m.distance_traveled), reverse=True)
                    primary_targets = candidates[:tower.target_count]

                line_index = tower.tile_index // 9
                line_buff_sum = self.line_step_buff[line_index]
                for other_tile, other_tower in self.towers.items():
                    if other_tile // 9 == line_index and "line_tower_buff" in other_tower.effects:
                        line_buff_sum += 0.20

                for _ in range(tower.attack_count):
                    for main_target in primary_targets:
                        if not main_target.alive:
                            continue

                        damage = tower.get_final_damage(
                            self.lap_count, self.global_element_buff, self.element_mult_buff,
                            line_buff=line_buff_sum, target_is_frozen=main_target.is_frozen
                        )
                        
                        if "burn_damage" in tower.effects:
                            damage += (damage * 0.20 * main_target.burn_stacks)

                        main_target.take_damage(damage)

                        if "double_burn" in tower.effects:
                            main_target.burn_stacks *= 2
                        if "burn" in tower.effects or "tile_burn" in tower.effects:
                            main_target.add_burn_stacks(1)
                        if "freeze" in tower.effects and main_target.freeze_immunity_cd == 0:
                            main_target.is_frozen = True
                        if "shock" in tower.effects:
                            main_target.is_shocked = True
                        if "tile_burn" in tower.effects:
                            if main_target.position not in self.tile_effects:
                                self.tile_effects[main_target.position] = {}
                            self.tile_effects[main_target.position]["fire"] = 1

                        extra_monsters = set()
                        if "aoe_tile" in tower.effects:
                            extra_monsters.update([m for m in self.monsters if m.alive and m.position == main_target.position and m != main_target])
                        if "explode" in tower.effects or ("burn_explode" in tower.effects and main_target.burn_stacks >= 20):
                            adj_tiles = [(main_target.position - 1) % 36, main_target.position, (main_target.position + 1) % 36]
                            extra_monsters.update([m for m in self.monsters if m.alive and m.position in adj_tiles and m != main_target])
                        if "chain_line" in tower.effects:
                            line_tiles = set(range((main_target.position // 9) * 9, ((main_target.position // 9) + 1) * 9))
                            extra_monsters.update([m for m in self.monsters if m.alive and m.position in line_tiles and m != main_target])
                        if "chain_tile" in tower.effects:
                            chain_tiles = [(main_target.position + i) % 36 for i in range(1, 4)]
                            extra_monsters.update([m for m in self.monsters if m.alive and m.position in chain_tiles and m != main_target])

                        for ex_mon in extra_monsters:
                            ex_mon.take_damage(damage)
                            if "burn" in tower.effects:
                                ex_mon.add_burn_stacks(1)
                            if "shock" in tower.effects:
                                ex_mon.is_shocked = True

                if tower.cooldown_max > 0:
                    tower.cooldown_timer = tower.cooldown_max

        # 6. 처치 판정 및 웨이브 진행
        dead_monsters = [m for m in self.monsters if not m.alive and m.hp <= 0]
        for m in dead_monsters:
            self.monsters.remove(m)
            self.total_kills += 1
            if self.current_wave <= 7:
                self.wave_kills += 1
                if self.wave_kills >= 5:
                    self.current_wave += 1
                    self.wave_kills = 0
                    self.pattern_index = 0
            elif m.is_boss:
                self.game_win = True

        # 7. 패배 판정
        if self.player_life <= 0:
            self.game_over = True
            self.failed_wave = self.current_wave

    def drop_feather_on_strongest_tower(self):
        active_towers = [t for t in self.towers.values() if not t.is_disabled_by_feather and t.can_attack]
        if active_towers:
            strongest = max(active_towers, key=lambda t: t.get_final_damage(self.lap_count, self.global_element_buff, self.element_mult_buff))
            strongest.is_disabled_by_feather = True

    def run_until_end(self, max_turns=300):
        while not self.game_over and not self.game_win and self.total_turns < max_turns:
            self.step_turn()
        return self.game_win, self.failed_wave

    def export_summary_dict(self, trial_id):
        tier1_cnt = sum(1 for t in self.towers.values() if t.tier == 1)
        tier2_cnt = sum(1 for t in self.towers.values() if t.tier == 2)
        tier3_cnt = sum(1 for t in self.towers.values() if t.tier == 3)
        alive_monsters_cnt = len([m for m in self.monsters if m.alive])

        # 수정 포인트: alive 여부와 관계없이 보스 객체에 남은 HP 수치를 직접 참조
        if self.boss_monster:
            boss_remaining_hp = max(0.0, round(self.boss_monster.hp, 1))
        else:
            boss_remaining_hp = -1.0

        summary = {
            "trial_id": trial_id,
            "result": "WIN" if self.game_win else "LOSS",
            "reached_wave": self.current_wave if not self.game_win else 8,
            "boss_remaining_hp": boss_remaining_hp,
            "total_kills": self.total_kills,
            "remaining_monsters": alive_monsters_cnt,
            "remaining_life": max(0, self.player_life),
            "total_turns": self.total_turns,
            "total_towers_count": len(self.towers),
            "tier1_count": tier1_cnt,
            "tier2_count": tier2_cnt,
            "tier3_count": tier3_cnt,
            "buff_add_Fire": self.global_element_buff["Fire"],
            "buff_add_Ice": self.global_element_buff["Ice"],
            "buff_add_Physics": self.global_element_buff["Physics"],
            "buff_add_Electric": self.global_element_buff["Electric"],
            "buff_mult_Fire": round(self.element_mult_buff["Fire"], 2),
            "buff_mult_Ice": round(self.element_mult_buff["Ice"], 2),
            "buff_mult_Physics": round(self.element_mult_buff["Physics"], 2),
            "buff_mult_Electric": round(self.element_mult_buff["Electric"], 2)
        }

        for idx in range(36):
            if idx in self.towers:
                t = self.towers[idx]
                dmg = round(t.get_final_damage(self.lap_count, self.global_element_buff, self.element_mult_buff), 1)
                summary[f"tile_{idx}_status"] = f"{t.id}_T{t.tier}(DMG:{dmg})"
            else:
                summary[f"tile_{idx}_status"] = "EMPTY"

        return summary

# ==========================================
# 4. GUI & 몬테카를로 시뮬레이터 인터페이스
# ==========================================

class SimulatorApp:
    def __init__(self, root):
        self.root = root
        self.root.title("개불빙 몬스터 밸런싱 시뮬레이터")
        self.root.geometry("680x670")

        ctrl_frame = ttk.LabelFrame(root, text=" 밸런스 수치 조정 (슬라이더) ")
        ctrl_frame.pack(fill="x", padx=10, pady=5)

        bear_base_hp = MONSTER_BASE_STATS.get("MON_001", {}).get("hp", 420)
        boss_base_hp = MONSTER_BASE_STATS.get("BOSS_001", {}).get("hp", 7000)
        boss_base_def = MONSTER_BASE_STATS.get("BOSS_001", {}).get("def", 350)

        self.sliders = {}
        self.create_slider(ctrl_frame, "bear_hp", "곰 초기 체력 (기준)", 100, 1000, bear_base_hp, 10)
        self.create_slider(ctrl_frame, "hp_mult", "웨이브 체력 Multiplier", 1.0, 2.5, DEFAULT_HP_MULT, 0.05)
        self.create_slider(ctrl_frame, "def_add", "웨이브 방어력 증가치", 0, 50, DEFAULT_DEF_ADD, 1)
        self.create_slider(ctrl_frame, "boss_hp", "까마귀 보스 체력", 1000, 30000, boss_base_hp, 500)
        self.create_slider(ctrl_frame, "boss_def", "까마귀 보스 방어력", 0, 500, boss_base_def, 10)
        self.create_slider(ctrl_frame, "sim_count", "시뮬레이션 반복 횟수", 100, 2000, 500, 100)

        info_frame = ttk.Frame(ctrl_frame)
        info_frame.pack(fill="x", padx=5, pady=5)

        self.lbl_w7_info = ttk.Label(info_frame, text="", font=("Consolas", 9, "bold"), foreground="#1D3557")
        self.lbl_w7_info.pack(side="left")

        self.update_w7_display()

        run_btn = ttk.Button(root, text="시뮬레이션 실행 및 CSV 누적 저장", command=self.run_simulation)
        run_btn.pack(fill="x", padx=10, pady=5)

        report_frame = ttk.LabelFrame(root, text=" 시뮬레이션 결과 리포트 ")
        report_frame.pack(fill="both", expand=True, padx=10, pady=5)

        self.txt_report = scrolledtext.ScrolledText(report_frame, wrap=tk.WORD, font=("Consolas", 10))
        self.txt_report.pack(fill="both", expand=True, padx=5, pady=5)

    def create_slider(self, parent, key, label_text, from_, to, default, resolution):
        frame = ttk.Frame(parent)
        frame.pack(fill="x", padx=5, pady=2)

        lbl = ttk.Label(frame, text=f"{label_text}:", width=22)
        lbl.pack(side="left")

        val_lbl = ttk.Label(frame, text=str(default), width=6)
        val_lbl.pack(side="right")

        var = tk.DoubleVar(value=default)

        def on_slider_change(v):
            val = float(v)
            val_lbl.config(text=f"{val:.2f}" if resolution < 1 else f"{int(val)}")
            if hasattr(self, 'lbl_w7_info'):
                self.update_w7_display()

        slider = ttk.Scale(frame, from_=from_, to=to, variable=var, command=on_slider_change)
        slider.pack(side="left", fill="x", expand=True, padx=5)

        self.sliders[key] = var

    def update_w7_display(self):
        bear_hp = self.sliders["bear_hp"].get()
        hp_mult = self.sliders["hp_mult"].get()
        scale = bear_hp / 420.0
        w7_mult = hp_mult ** 6
        
        bear_w7 = bear_hp * w7_mult
        fox_w7 = (MONSTER_BASE_STATS.get("MON_002", {}).get("hp", 300) * scale) * w7_mult
        sq_w7 = (MONSTER_BASE_STATS.get("MON_003", {}).get("hp", 240) * scale) * w7_mult
        
        self.lbl_w7_info.config(
            text=f"▶ W7 몬스터 체력 현황 | 곰: {bear_w7:,.0f} | 여우: {fox_w7:,.0f} | 다람쥐: {sq_w7:,.0f}"
        )

    def run_simulation(self):
        bear_hp = self.sliders["bear_hp"].get()
        hp_mult = self.sliders["hp_mult"].get()
        def_add = self.sliders["def_add"].get()
        boss_hp = self.sliders["boss_hp"].get()
        boss_def = self.sliders["boss_def"].get()
        sim_count = int(self.sliders["sim_count"].get())

        wins = 0
        losses = 0
        wave_deaths = {1: 0, 2: 0, 3: 0, 4: 0, 5: 0, 6: 0, 7: 0, 8: 0}

        self.txt_report.delete("1.0", tk.END)
        self.txt_report.insert(tk.END, f"시뮬레이션 진행 중... (총 {sim_count}회)\n")
        self.root.update()

        all_trials_data = []

        for trial_i in range(1, sim_count + 1):
            sim = GameSimulation(bear_hp, hp_mult, def_add, boss_hp, boss_def)
            is_win, failed_wave = sim.run_until_end()
            
            if is_win:
                wins += 1
            else:
                losses += 1
                if failed_wave in wave_deaths:
                    wave_deaths[failed_wave] += 1

            all_trials_data.append(sim.export_summary_dict(trial_i))

        csv_filename = "simulation_results.csv"
        file_exists = os.path.exists(csv_filename)

        with open(csv_filename, mode="a", newline="", encoding="utf-8-sig") as f:
            if file_exists:
                f.write("\n")

            param_header = f"# Parameters: bear_hp={bear_hp:.0f}, hp_mult={hp_mult:.2f}, def_add={def_add:.0f}, boss_hp={boss_hp:.0f}, boss_def={boss_def:.0f}, sim_count={sim_count}\n"
            f.write(param_header)

            if all_trials_data:
                fieldnames = list(all_trials_data[0].keys())
                writer = csv.DictWriter(f, fieldnames=fieldnames)
                writer.writeheader()
                writer.writerows(all_trials_data)

        win_rate = (wins / sim_count) * 100.0
        abs_path = os.path.abspath(csv_filename)

        report = []
        report.append("==================================================")
        report.append(f" [시뮬레이션 결과 리포트] (총 {sim_count}회)")
        report.append("==================================================")
        report.append(f"▶ 최종 승률: {win_rate:.2f}% ({wins}승 / {losses}패)")
        report.append(f"▶ CSV 파일 누적 저장 완료: {abs_path}")
        report.append("--------------------------------------------------")
        report.append("▶ 웨이브별 플레이어 패배(사망) 분포:")
        
        for w in range(1, 8):
            d_cnt = wave_deaths[w]
            d_pct = (d_cnt / sim_count) * 100.0
            report.append(f"  - Wave {w}: {d_cnt}회 ({d_pct:.2f}%)")
        
        boss_d_cnt = wave_deaths[8]
        boss_d_pct = (boss_d_cnt / sim_count) * 100.0
        report.append(f"  - Wave 8 (보스전): {boss_d_cnt}회 ({boss_d_pct:.2f}%)")
        
        report.append("--------------------------------------------------")
        report.append("▶ 밸런싱 피드백:")
        if win_rate > 60:
            report.append("  [!] 승률이 너무 높습니다. Multiplier나 방어력을 올려 난이도를 올리세요.")
        elif win_rate < 20:
            report.append("  [!] 승률이 너무 낮습니다. 초기 몬스터 체력이나 Multiplier를 낮추세요.")
        else:
            report.append("  [*] 적절한 목표 승률 구간(20% ~ 60%)에 진입해 있습니다.")
        report.append("==================================================")

        self.txt_report.delete("1.0", tk.END)
        self.txt_report.insert(tk.END, "\n".join(report))

# ==========================================
# 5. 실행부
# ==========================================
if __name__ == "__main__":
    root = tk.Tk()
    app = SimulatorApp(root)
    root.mainloop()