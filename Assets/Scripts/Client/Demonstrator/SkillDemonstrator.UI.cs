using System;
using System.Linq;
using UnityEngine;

namespace StrategyCore
{
    public partial class SkillDemonstrator
    {
        GUIStyle titleStyle, bodyStyle, smallStyle, buttonStyle, headingStyle;
        readonly Color cyan=new Color(.28f,.88f,.87f), ink=new Color(.045f,.065f,.085f,.95f);
        void Styles()
        {
            if(titleStyle!=null)return;
            titleStyle=new GUIStyle(GUI.skin.label){fontSize=28,fontStyle=FontStyle.Bold};titleStyle.normal.textColor=Color.white;
            headingStyle=new GUIStyle(titleStyle){fontSize=19,wordWrap=true};
            bodyStyle=new GUIStyle(GUI.skin.label){fontSize=16,wordWrap=true};bodyStyle.normal.textColor=new Color(.89f,.93f,.96f);
            smallStyle=new GUIStyle(bodyStyle){fontSize=13};smallStyle.normal.textColor=new Color(.65f,.74f,.79f);
            buttonStyle=new GUIStyle(GUI.skin.button){fontSize=15,wordWrap=true,padding=new RectOffset(12,12,7,7)};
        }
        void Panel(Rect r,Color c){var old=GUI.color;GUI.color=c;GUI.DrawTexture(r,Texture2D.whiteTexture);GUI.color=old;}
        bool Button(string label)=>GUILayout.Button(label,buttonStyle,GUILayout.MinHeight(34));
        void OnGUI()
        {
            var e=Event.current;
            if(e.type==EventType.KeyDown){
                if(e.keyCode==KeyCode.H){film=!film;e.Use();}
                else if(e.keyCode==KeyCode.R){pendingReset=true;e.Use();}
                else if(e.keyCode==KeyCode.Space){paused=!paused;Time.timeScale=paused?0:speed;e.Use();}
            }
            Styles();var matrix=GUI.matrix;GUI.matrix=Matrix4x4.Scale(new Vector3(Screen.width/1600f,Screen.height/900f,1));
            Panel(new Rect(0,0,1600,84),ink);
            GUI.Label(new Rect(24,10,470,40),"LINEWARS  /  ПОЛИГОН",titleStyle);
            GUI.Label(new Rect(26,51,1050,24),Ready&&roster!=null?roster[selected].prefab.unitName+"  ·  "+(specialization<0?"Базовые умения":roster[selected].specializations[specialization].displayName):"Запуск локального матча…",bodyStyle);
            GUI.Label(new Rect(1080,25,282,32),"R — сброс  ·  пробел — "+(paused?"продолжить":"пауза"),smallStyle);
            if(GUI.Button(new Rect(1370,20,205,38),film?"Управление [H]":"Режим видео [H]",buttonStyle))film=!film;
            Panel(new Rect(film?300:322,810,film?1000:886,64),ink);
            GUI.Label(new Rect(film?320:340,821,film?960:850,48),headline,headingStyle);
            if(!Ready||roster==null){GUI.matrix=matrix;return;}
            if(!film){RosterPanel();SkillsPanel();Controls();}
            DrawActorLabels();
            GUI.matrix=matrix;
        }
        void RosterPanel()
        {
            Panel(new Rect(16,100,280,778),ink);
            GUILayout.BeginArea(new Rect(30,113,252,750));
            GUILayout.Label("РОСТЕР  /  28",headingStyle);
            GUILayout.Label("Выбор пересоздаёт опыт на полигоне",smallStyle);
            rosterScroll=GUILayout.BeginScrollView(rosterScroll);
            for(int i=0;i<roster.Length;i++){
                GUI.backgroundColor=i==selected?cyan:Color.white;
                if(Button((i+1).ToString("00")+"  "+roster[i].prefab.unitName))pendingSelection=i;
            }
            GUI.backgroundColor=Color.white;GUILayout.EndScrollView();GUILayout.EndArea();
        }
        void SkillsPanel()
        {
            Panel(new Rect(1224,100,360,778),ink);
            GUILayout.BeginArea(new Rect(1240,114,328,746));
            GUILayout.Label("СПОСОБНОСТИ",headingStyle);
            if(!Caster){GUILayout.Label("Подготовка юнитов…",bodyStyle);GUILayout.EndArea();return;}
            GUILayout.Label("HP "+Mathf.CeilToInt(Caster.health)+" / "+Mathf.CeilToInt(Caster.maxHealth)+"     Ресурс "+Mathf.CeilToInt(Caster.mana),bodyStyle);
            skillScroll=GUILayout.BeginScrollView(skillScroll);
            foreach(var a in Caster.abilities.Where(a=>a&&!a.name.Contains("UpgradePassive"))){
                int i=Array.IndexOf(Caster.abilities,a);float cd=Cooldown(i);bool locked=i<Caster.abilityLocked.Length&&Caster.abilityLocked[i];
                GUILayout.Space(10);GUILayout.Label(Name(a),headingStyle);
                string desc=a.description!=null&&a.description.Length>0?a.description[0]:"";
                if(!string.IsNullOrEmpty(desc))GUILayout.Label(desc,smallStyle);
                if(a is CompositeSkill){
                    GUI.enabled=!locked&&cd<=0&&!Caster.dead&&!rebuilding;
                    if(Button(locked?"Закрыто специализацией":cd>0?"Перезарядка  "+cd.ToString("0.0")+" с":"Применить")){
                        autoShow=false;
                        if(!Cast(a))Note("Не выполнены условия: проверьте цель, ресурс или текущую форму.");
                    }
                    GUI.enabled=true;
                }else GUILayout.Label(locked?"Закрыто специализацией":"Пассивное — срабатывает при выполнении условия",smallStyle);
            }
            GUILayout.Space(18);GUILayout.Label("КАК ПРОВЕРЯТЬ",headingStyle);
            GUILayout.Label(roster[selected].notes,smallStyle);
            GUILayout.Space(14);GUILayout.Label("СПЕЦИАЛИЗАЦИЯ",headingStyle);
            if(Button((specialization<0?"✓ ":"")+"Без специализации")){specialization=-1;pendingReset=true;}
            for(int i=0;i<roster[selected].specializations.Length;i++)if(Button((i==specialization?"✓ ":"")+roster[selected].specializations[i].displayName)){specialization=i;pendingReset=true;}
            GUILayout.Label("Выбор действует только в этом опыте. Боевой баланс и дерево технологий не сохраняются.",smallStyle);
            GUILayout.EndScrollView();GUILayout.EndArea();
        }
        void Controls()
        {
            Panel(new Rect(322,650,886,146),ink);
            GUILayout.BeginArea(new Rect(338,662,854,126));
            GUILayout.BeginHorizontal();
            if(Button(autoShow?"Автопоказ: ВКЛ":"Автопоказ: ВЫКЛ")){autoShow=!autoShow;nextAuto=Time.time;}
            if(Button(attacks?"Остановить атаку":"Обычная атака")){attacks=!attacks;if(!attacks&&Caster&&!Caster.dead){Caster.canAttack=false;Caster.Hold();}}
            if(Button(retaliation?"Прекратить ответ":"Ответная атака")){retaliation=!retaliation;if(!retaliation)foreach(var u in enemies)if(u&&!u.dead){u.canAttack=false;u.Hold();}}
            if(Button("Сброс опыта"))pendingReset=true;
            GUILayout.EndHorizontal();GUILayout.BeginHorizontal();
            if(Button("HP юнита → 20%")){autoShow=false;if(Caster&&!Caster.dead)Caster.SetHP(Caster.maxHealth*.2f);}
            if(Button("Входящий удар 25%"))Damage(Caster,.25f);
            if(Button("Добить цель"))Damage(enemies.FirstOrDefault(u=>u&&!u.dead),10);
            if(Button("Гибель союзника")){var u=allies.FirstOrDefault(a=>a&&!a.dead);if(u)u.Die(foeOwner,enemies.FirstOrDefault(a=>a&&!a.dead),false);}
            GUILayout.EndHorizontal();GUILayout.BeginHorizontal();
            if(Button("Ресурс + откаты")){if(Caster&&!Caster.dead){Caster.SetMP(Caster.maxMana);for(int i=0;i<Caster.abilities.Length;i++)Caster.ChangeAbilityCooldown(-1,i,false);}}
            if(Button("Гибель юнита")){if(Caster&&!Caster.dead)Caster.Die(foeOwner,enemies.FirstOrDefault(a=>a&&!a.dead),false);}
            string[] labels={"Группа","Дуэль","Линия","Здание"};
            if(Button("Цели: "+labels[formation])){formation=(formation+1)%4;pendingReset=true;}
            if(Button("Скорость: "+speed.ToString("0.0")+"×")){speed=speed==1?.5f:speed==.5f?1.5f:1;Time.timeScale=paused?0:speed;}
            GUILayout.EndHorizontal();GUILayout.EndArea();
            GUI.Label(new Rect(340,105,850,24),"Мишени: увеличенный запас HP, обычные сопротивления  ·  1 союзник ранен до 20% HP",smallStyle);
            zoom=GUI.HorizontalSlider(new Rect(1020,136,160,18),zoom,5,13);
            GUI.Label(new Rect(914,128,102,24),"Камера",smallStyle);
        }
        void DrawActorLabels()
        {
            if(!stageCamera)return;
            foreach(var u in actors){if(!u||u.dead)continue;var p=stageCamera.WorldToScreenPoint(u.transform.position+Vector3.up*(u.unitHeight+.5f));if(p.z<0)continue;
                float x=p.x/Screen.width*1600,y=(1-p.y/Screen.height)*900;
                if(!film&&(x<310||x>1210||y>620))continue;
                var rect=new Rect(x-60,y,120,22);Panel(rect,new Color(.025f,.04f,.05f,.75f));
                var old=GUI.color;GUI.color=u==Caster?cyan:u.owner==0?new Color(.6f,.85f,1):new Color(1,.68f,.48f);
                GUI.Label(rect,Mathf.CeilToInt(u.health)+" HP",smallStyle);GUI.color=old;
            }
        }
    }
}
