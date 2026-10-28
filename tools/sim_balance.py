"""《举火》数值验证模拟器 v0.2
1) 王朝经济/民心/天命：三套施政人格跑 30 年（360 月）
2) 战斗推演：验证战力公式、克制与数量优势
运行：python tools/sim_balance.py
"""
def clamp(v, lo, hi):
    return max(lo, min(hi, v))

YEARS, MONTHS = 30, 360

class Realm:
    """一个简化王朝：人口、粮、钱、民怨、天命、兼并。"""
    def __init__(self, name, tax=0.12, oversight=30, army_ratio=1/22,
                 relief=0.0, reform_year=None, tyranny=False):
        self.name = name
        self.tax = tax
        self.oversight = oversight
        self.army_ratio = army_ratio
        self.relief = relief
        self.reform_year = reform_year
        self.tyranny = tyranny

        self.pop = 240_000
        self.granary = 600_000
        self.treasury = 80_000
        self.grievance = 45.0
        self.mandate = 45.0
        self.corruption = 0.11
        self.gentry = 60.0
        self.registered = 1.0
        self.land = 1.0
        self.history = []
        self.collapsed = None

    def step(self, m):
        year, month = m // 12 + 1, m % 12 + 1

        if self.reform_year and year == self.reform_year and month == 1:
            self.registered *= 1.18
            self.land = min(1.0, self.land + 0.10)
            self.gentry -= 15
            self.grievance += 6

        if month == 1:
            self.land = max(0.55, self.land - 0.005)

        prod_year = self.pop * 4.5 * 2.0 * self.land
        seasonal = {1:.2,2:.3,3:.5,4:.8,5:1.2,6:1.4,7:1.6,8:2.4,9:3.0,10:2.2,11:.6,12:.3}[month]
        self.granary += prod_year / 12 * seasonal

        need = self.pop * 7.3 / 12
        if self.granary >= need:
            self.granary = (self.granary - need) * 0.992
            famine = False
        else:
            famine = True
            self.granary = 0
            self.pop *= 0.995
            self.grievance += 6

        collection = clamp(0.70 + 0.20*self.oversight/100 - (60-self.gentry)/500, 0.35, 0.95)
        if month == 10:
            tax_grain = prod_year * self.tax * self.registered
            tax_cash = tax_grain * 0.8
            misc = tax_cash * 0.55
            gross = tax_cash + misc
            self.corruption = 0.30 * 0.45 * (1 - 0.5*self.oversight/100)
            self.treasury += gross * collection * (1 - self.corruption)

        officials = self.pop / 300
        army = self.pop * self.army_ratio
        payroll = officials * 45 / 12
        military = army * 11 / 12
        royal = (12_000 + self.pop * 0.015) / 12
        works = 1500
        self.treasury -= (payroll + military + royal + works + self.relief)

        relief_k = min(1.0, self.relief / 6000)
        eq = (38 + 3.2*(self.tax*100 - 12)
              + 12*(1 - relief_k)
              + 10*(1 - self.oversight/100)
              - 8*(self.gentry - 60)/40)
        if famine:
            eq += 15
        eq = clamp(eq, 0, 99)
        self.grievance = clamp(self.grievance + (eq - self.grievance)*0.06, 0, 100)

        self.gentry = clamp(self.gentry + (0.12 if self.tax < 0.13 else -0.20), 0, 100)

        virtue = 0.3 if self.tyranny else 0.8
        tyranny_v = max(0, (self.grievance - 60)/40)
        self.mandate = clamp(self.mandate + 0.15*virtue
                             + 0.08*(100 - self.grievance - 50)/10
                             - 0.5*tyranny_v, 0, 100)

        birth = self.pop * 0.0020 * (1 + clamp(self.granary/(need*8), 0, 1)*0.5)
        death = self.pop * (0.0018 + (0.006 if famine else 0))
        self.pop += birth - death

        if self.collapsed is None:
            if self.treasury < -400_000:
                self.collapsed = (year, '财政崩溃')
            elif self.grievance >= 95:
                self.collapsed = (year, '民变')
            elif self.mandate <= 2:
                self.collapsed = (year, '天命归零')

        if month == 12:
            self.history.append(dict(year=year, treasury=self.treasury, granary=self.granary,
                                     pop=self.pop, grievance=self.grievance,
                                     mandate=self.mandate, land=self.land))

    def run(self):
        for m in range(MONTHS):
            self.step(m)
            if self.collapsed:
                break
        return self

def report_economy():
    cases = [
        Realm('A 保守：低税·低监察·不改革', tax=0.12, oversight=25),
        Realm('B 改革：中税·高监察·第6年括户', tax=0.14, oversight=60, relief=2500, reform_year=6),
        Realm('C 暴政：高税·扩军·高压', tax=0.20, oversight=15, army_ratio=1/12, tyranny=True),
    ]
    print('=' * 84)
    print('一、王朝经济模拟（30 年 / 月推进）')
    print('=' * 84)
    print(f"{'施政':30}{'首年':>9}{'第10年':>9}{'第20年':>9}{'末年':>9}{'结局':>18}")
    for c in cases:
        c.run()
        h = c.history
        def g(y):
            for r in h:
                if r['year'] == y:
                    return f"{r['treasury']/10000:.1f}"
            return '—'
        end = f"{h[-1]['treasury']/10000:.1f}" if h else '—'
        tail = (c.collapsed[1] + '·第' + str(c.collapsed[0]) + '年') if c.collapsed else '存续'
        print(f"{c.name:30}{g(1):>9}{g(10):>9}{g(20):>9}{end:>9}{tail:>18}")

    print()
    print(f"{'施政':30}{'末民心':>8}{'末天命':>8}{'末人口':>10}{'可税田':>8}")
    for c in cases:
        e = c.history[-1] if c.history else dict(grievance=0, mandate=0, pop=0, land=0)
        print(f"{c.name:30}{100-e['grievance']:>8.0f}{e['mandate']:>8.0f}"
              f"{e['pop']/10000:>9.1f}万{e['land']*100:>7.0f}%")

    print()
    print('B 改革路线明细（万贯 / 万石 / 万人）：')
    print(f"{'年':>4}{'国库':>9}{'存粮':>9}{'民怨':>7}{'天命':>7}{'人口':>8}{'可税田':>8}")
    for r in cases[1].history:
        if r['year'] <= 3 or r['year'] % 3 == 0:
            print(f"{r['year']:>4}{r['treasury']/10000:>9.1f}{r['granary']/10000:>9.1f}"
                  f"{r['grievance']:>7.1f}{r['mandate']:>7.1f}{r['pop']/10000:>8.2f}{r['land']*100:>7.0f}%")

# ================= 二、战斗推演 =================
UNITS = {
    '枪兵': dict(atk=1.00, dfn=1.10, cost=1.0, maint=1.0),
    '刀兵': dict(atk=1.10, dfn=1.00, cost=1.0, maint=1.0),
    '弓兵': dict(atk=1.20, dfn=0.80, cost=1.3, maint=1.1),
    '骑兵': dict(atk=1.40, dfn=1.00, cost=3.0, maint=3.2),
    '重甲': dict(atk=0.90, dfn=1.50, cost=2.2, maint=2.0),
    '器械': dict(atk=1.60, dfn=0.60, cost=4.0, maint=2.5),
}
COUNTER = {('枪兵','骑兵'):1.35, ('骑兵','弓兵'):1.35, ('弓兵','枪兵'):1.15,
           ('骑兵','枪兵'):0.75, ('弓兵','骑兵'):0.75, ('枪兵','弓兵'):0.90}

def cp(n, u, train=60, equip=60, morale=60, gen=1.0, terr=1.0, counter=1.0):
    return (n ** 0.92) * (0.55+train/100*0.9) * (0.55+equip/100*0.9) \
           * (0.5+morale/100) * gen * terr * counter * UNITS[u]['atk']

def battle(a, na, b, nb, **kw):
    ga, gb = kw.get('gen_a',1.0), kw.get('gen_b',1.0)
    ta, tb = kw.get('terr_a',1.0), kw.get('terr_b',1.0)
    tr, eq, mo = kw.get('train',60), kw.get('equip',60), kw.get('morale',60)
    ca = cp(na, a, tr, eq, mo, ga, ta, COUNTER.get((a,b),1.0))
    cb = cp(nb, b, tr, eq, mo, gb, tb, COUNTER.get((b,a),1.0))
    la = min(na, na*0.34*(cb/(ca+cb))*2)
    lb = min(nb, nb*0.34*(ca/(ca+cb))*2)
    return ca, cb, la, lb

def report_battle():
    print()
    print('=' * 84)
    print('二、战斗推演（同等训练/装备/士气）')
    print('=' * 84)
    print(f"{'攻方':20}{'守方':20}{'攻CP':>8}{'守CP':>8}{'攻损':>7}{'守损':>7}{'判定':>7}")
    cases = [('枪兵',1000,'骑兵',800), ('骑兵',800,'枪兵',1000),
             ('弓兵',1000,'枪兵',1000), ('重甲',800,'弓兵',1200),
             ('刀兵',1000,'刀兵',1000), ('器械',600,'重甲',800)]
    for a,na,b,nb in cases:
        ca,cb,la,lb = battle(a,na,b,nb)
        win = '攻胜' if (na-la) > (nb-lb) else '守胜'
        print(f"{a+' ×'+str(na):20}{b+' ×'+str(nb):20}{ca:>8.0f}{cb:>8.0f}{la:>7.0f}{lb:>7.0f}{win:>7}")

    print()
    print('数量优势（同兵种刀兵）：')
    for n in (1000,1200,1500,2000,3000):
        ca,cb,la,lb = battle('刀兵',n,'刀兵',1000)
        print(f'  {n:>4} vs 1000 → 攻损 {la:>4.0f}｜守损 {lb:>4.0f}｜剩余兵力比 {(n-la)/(1000-lb):.2f}')

    print()
    print('将领与地形（1000 枪 vs 1000 骑）：')
    for g,t in [(1.0,1.0),(1.3,1.0),(1.0,0.6),(1.3,0.6)]:
        ca,cb,la,lb = battle('枪兵',1000,'骑兵',1000,gen_a=g,terr_a=t)
        print(f'  将×{g} 地×{t} → 攻损 {la:>4.0f}｜守损 {lb:>4.0f}｜CP比 {ca/cb:.2f}')

if __name__ == '__main__':
    report_economy()
    report_battle()
