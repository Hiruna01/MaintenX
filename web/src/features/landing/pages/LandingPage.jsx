import { ArrowUpRight } from 'lucide-react';
import { Link } from 'react-router-dom';

import BrandMark from '../../../components/shell/BrandMark';
import FeatureRows from '../components/FeatureRows';
import HeroStage from '../components/HeroStage';
import LoopGrid from '../components/LoopGrid';
import RolesSection from '../components/RolesSection';
import TrustSection from '../components/TrustSection';
import styles from '../landing.module.css';

/** The one way in. Every "Sign in" on the page goes to the existing login page. */
function SignInLink({ className, children = 'Sign in' }) {
  return (
    <Link to="/login" className={className}>
      {children}
      <ArrowUpRight size={18} strokeWidth={1.9} aria-hidden="true" />
    </Link>
  );
}

/**
 * The first page a signed-out visitor sees at `/`: what MaintenX is, the loop it runs, why its
 * answers can be trusted, and who does what — then Sign in, which goes to /login.
 *
 * Static, and deliberately so: it calls no API and states no number it cannot stand behind (the
 * rule numbers are the API's defaults, kept in landingFacts.js). There is no sign-up — accounts
 * are issued — so Sign in is the only action on the page.
 */
export function LandingPage() {
  return (
    <div className={styles.page}>
      <a href="#main" className={styles.skip}>
        Skip to content
      </a>

      <header className={styles.navWrap}>
        <nav className={styles.nav} aria-label="Main">
          <a href="#top" className={styles.brand}>
            <span className={styles.brandMark}>
              <BrandMark />
            </span>
            MaintenX
          </a>
          <div className={styles.navLinks}>
            <a href="#loop">How it works</a>
            <a href="#trust">Why trust it</a>
            <a href="#roles">For your role</a>
          </div>
          <SignInLink className={styles.navCta} />
        </nav>
      </header>

      <main id="main" className={styles.main}>
        <section id="top" className={styles.hero} aria-labelledby="hero-title">
          <span className={styles.heroBadge}>
            <span className={styles.heroBadgeNew}>New</span>
            Campus maintenance that checks its own work
          </span>
          <h1 id="hero-title" className={styles.h1}>
            <span className={styles.quiet}>Most faults get fixed.</span>
            <span>Few get checked.</span>
          </h1>
          <p className={styles.heroLead}>
            MaintenX turns a campus fault report into a diagnosed, approved, scheduled — and <strong>verified</strong> —
            repair. Scan the sticker, answer two questions, and we take it from there.
          </p>
          <div className={styles.ctaRow}>
            <SignInLink className={styles.ctaPrimary} />
            <a href="#loop" className={styles.ctaSecondary}>
              See how it works
            </a>
          </div>
          <HeroStage />
        </section>

        <section className={styles.statement} aria-label="What MaintenX believes">
          <p>
            <span className={styles.quiet}>A repair isn’t done when the technician leaves.</span> It’s done when the fault
            stays gone.
          </p>
        </section>

        <LoopGrid />
        <FeatureRows />
        <TrustSection />
        <RolesSection />

        <section className={styles.closing} aria-labelledby="closing-title">
          <h2 id="closing-title" className={styles.h2Large}>
            <span className={styles.closingQuiet}>Report it once.</span>
            <span>We’ll make sure it stays fixed.</span>
          </h2>
          <p className={styles.closingLead}>Accounts are issued by your Facilities team — there’s nothing to sign up for.</p>
          <div className={styles.ctaRow}>
            <SignInLink className={styles.ctaLight} />
            <a href="#loop" className={styles.ctaOutline}>
              See how it works
            </a>
          </div>
        </section>
      </main>

      <footer className={styles.footer}>
        <span className={styles.footerBrand}>MaintenX</span>
        <span>Campus facilities — reports, repairs, and the check that they held.</span>
        <Link to="/login" className={styles.footerLink}>
          Sign in
        </Link>
      </footer>
    </div>
  );
}

export default LandingPage;
