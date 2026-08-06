(() => {
  const config = window.TODOX_LANDING_CONFIG || {};
  const dashboardUrl = config.dashboardUrl || "https://dashboard.todox.vn";
  document.querySelectorAll("[data-dashboard-link]").forEach(link => {
    link.href = dashboardUrl;
    link.rel = "noopener";
  });
})();
AOS.init({
      duration: 850,
      once: true,
      offset: 80,
      easing: "ease-out-cubic"
    });

    const swiper = new Swiper(".industrySwiper", {
      slidesPerView: 1.15,
      spaceBetween: 18,
      loop: true,
      autoplay: { delay: 3200, disableOnInteraction: false },
      pagination: { el: ".swiper-pagination", clickable: true },
      navigation: { nextEl: ".swiper-button-next", prevEl: ".swiper-button-prev" },
      breakpoints: {
        700: { slidesPerView: 2.2 },
        1050: { slidesPerView: 3.25 }
      }
    });

    const header = document.getElementById("header");
    const backTop = document.getElementById("backTop");
    window.addEventListener("scroll", () => {
      header.classList.toggle("scrolled", window.scrollY > 20);
      backTop.classList.toggle("show", window.scrollY > 700);
    });
    backTop.addEventListener("click", () => window.scrollTo({ top: 0, behavior: "smooth" }));

    const menuBtn = document.getElementById("menuBtn");
    const navLinks = document.getElementById("navLinks");
    menuBtn.addEventListener("click", () => navLinks.classList.toggle("open"));
    navLinks.querySelectorAll("a").forEach(a => a.addEventListener("click", () => navLinks.classList.remove("open")));

    const counters = document.querySelectorAll("[data-count]");
    const counterObserver = new IntersectionObserver(entries => {
      entries.forEach(entry => {
        if (!entry.isIntersecting) return;
        const el = entry.target;
        const target = Number(el.dataset.count);
        let value = 0;
        const duration = 1200;
        const step = Math.max(1, Math.round(target / (duration / 20)));
        const timer = setInterval(() => {
          value += step;
          if (value >= target) {
            value = target;
            clearInterval(timer);
          }
          el.textContent = value + (target === 98 ? "%" : "+");
        }, 20);
        counterObserver.unobserve(el);
      });
    }, { threshold: .6 });
    counters.forEach(c => counterObserver.observe(c));


    // AI Neural Workflow — deterministic loop + callable test function
    gsap.registerPlugin(ScrollTrigger);

    const workflowPath = document.getElementById("workflowProgressPath");
    const workflowNodes = gsap.utils.toArray(".workflow-node");
    const workflowChips = gsap.utils.toArray(".ai-data-chip");
    const workflowSection = document.getElementById("aiWorkflow");

    let workflowTL = null;
    let workflowIsActive = false;
    let workflowLoopTimer = null;
    let workflowRunId = 0;

    function clearWorkflowTimer() {
      if (workflowLoopTimer !== null) {
        window.clearTimeout(workflowLoopTimer);
        workflowLoopTimer = null;
      }
    }

    function resetWorkflowVisuals() {
      if (!workflowPath) return;

      const pathLength = workflowPath.getTotalLength();

      gsap.killTweensOf(workflowPath);
      gsap.killTweensOf(workflowNodes);
      gsap.killTweensOf(workflowChips);

      gsap.set(workflowPath, {
        strokeDasharray: pathLength,
        strokeDashoffset: pathLength
      });

      gsap.set(["#workflowPulse", "#workflowPulse2"], {
        autoAlpha: 0
      });

      gsap.set(workflowNodes, {
        autoAlpha: 0.16,
        scale: 0.9,
        y: 18,
        filter: "grayscale(.9) brightness(.55)"
      });

      gsap.set(workflowChips, {
        autoAlpha: 0.18,
        y: 8
      });

      workflowNodes.forEach(node => node.classList.remove("is-active"));
    }

    function buildWorkflowTimeline(runId) {
      if (!workflowPath) return null;

      const duration = 4.0;
      const stepTimes = [0.12, 0.85, 1.60, 2.35, 3.10];

      const tl = gsap.timeline({
        paused: true,
        onComplete: () => {
          if (!workflowIsActive || runId !== workflowRunId) return;

          clearWorkflowTimer();
          workflowLoopTimer = window.setTimeout(() => {
            if (!workflowIsActive || runId !== workflowRunId) return;
            window.startTodoXWorkflow();
          }, 3000);
        }
      });

      tl.to(workflowPath, {
        strokeDashoffset: 0,
        duration,
        ease: "none"
      }, 0);

      workflowNodes.forEach((node, index) => {
        const at = stepTimes[index];

        tl.call(() => {
          if (runId !== workflowRunId) return;
          node.classList.add("is-active");
        }, null, at);

        tl.to(node, {
          autoAlpha: 1,
          scale: 1,
          y: 0,
          filter: "grayscale(0) brightness(1)",
          duration: 0.65,
          ease: "power2.out"
        }, at);

        if (workflowChips[index]) {
          tl.to(workflowChips[index], {
            autoAlpha: 0.86,
            y: 0,
            duration: 0.4,
            ease: "power2.out"
          }, at + 0.15);
        }
      });

      // Keep the timeline alive until the line has fully reached node 05.
      tl.to({}, { duration: 0.2 }, duration + 0.05);

      return tl;
    }

    window.startTodoXWorkflow = function startTodoXWorkflow(force = false) {
      if (!force && !workflowIsActive) return;

      clearWorkflowTimer();
      workflowRunId += 1;
      const runId = workflowRunId;

      if (workflowTL) {
        workflowTL.kill();
        workflowTL = null;
      }

      resetWorkflowVisuals();
      workflowTL = buildWorkflowTimeline(runId);

      // Next frame ensures SVG dash reset is painted before the line starts.
      window.requestAnimationFrame(() => {
        window.requestAnimationFrame(() => {
          if (!workflowTL || runId !== workflowRunId) return;
          workflowTL.play(0);
        });
      });
    };

    window.stopTodoXWorkflow = function stopTodoXWorkflow() {
      clearWorkflowTimer();
      workflowRunId += 1;

      if (workflowTL) {
        workflowTL.kill();
        workflowTL = null;
      }

      resetWorkflowVisuals();
    };

    // Clicking node 01 always restarts the workflow for quick testing.
    const firstWorkflowNode = document.querySelector('.workflow-node[data-step="1"]');
    if (firstWorkflowNode) {
      firstWorkflowNode.setAttribute("role", "button");
      firstWorkflowNode.setAttribute("tabindex", "0");
      firstWorkflowNode.setAttribute("title", "Nhấn để chạy lại workflow");

      firstWorkflowNode.addEventListener("click", () => {
        workflowIsActive = true;
        window.startTodoXWorkflow(true);
      });

      firstWorkflowNode.addEventListener("keydown", event => {
        if (event.key === "Enter" || event.key === " ") {
          event.preventDefault();
          workflowIsActive = true;
          window.startTodoXWorkflow(true);
        }
      });
    }

    if (window.matchMedia("(prefers-reduced-motion: reduce)").matches) {
      if (workflowPath) gsap.set(workflowPath, { strokeDashoffset: 0 });
      gsap.set(workflowNodes, { autoAlpha: 1, scale: 1, y: 0, filter: "none" });
      gsap.set(workflowChips, { autoAlpha: 0.86, y: 0 });
      workflowNodes.forEach(node => node.classList.add("is-active"));
    } else if (workflowSection) {
      resetWorkflowVisuals();

      const workflowObserver = new IntersectionObserver(entries => {
        entries.forEach(entry => {
          const rect = entry.boundingClientRect;
          const vh = window.innerHeight || document.documentElement.clientHeight;
          const activeZone =
            entry.isIntersecting &&
            rect.top < vh * 0.78 &&
            rect.bottom > vh * 0.22;

          if (activeZone && !workflowIsActive) {
            workflowIsActive = true;
            window.startTodoXWorkflow();
          } else if (!activeZone && workflowIsActive) {
            workflowIsActive = false;
            window.stopTodoXWorkflow();
          }
        });
      }, { threshold: [0, 0.05, 0.15, 0.3] });

      workflowObserver.observe(workflowSection);

      document.addEventListener("visibilitychange", () => {
        if (document.hidden) {
          if (workflowTL) workflowTL.pause();
          clearWorkflowTimer();
        } else if (workflowIsActive) {
          window.startTodoXWorkflow();
        }
      });
    }

    // Neural particles background
    if (window.tsParticles) {
      tsParticles.load({
        id: "workflowParticles",
        options: {
          fullScreen: { enable: false },
          background: { color: { value: "transparent" } },
          fpsLimit: 50,
          particles: {
            number: {
              value: window.innerWidth < 700 ? 24 : 52,
              density: { enable: true, area: 900 }
            },
            color: { value: ["#ffbd22", "#ffd86b", "#ffffff"] },
            shape: { type: "circle" },
            opacity: { value: { min: 0.12, max: 0.48 } },
            size: { value: { min: 1, max: 3 } },
            links: {
              enable: true,
              distance: 135,
              color: "#ffbd22",
              opacity: 0.12,
              width: 1
            },
            move: {
              enable: true,
              speed: 0.48,
              direction: "none",
              random: true,
              straight: false,
              outModes: { default: "bounce" }
            }
          },
          interactivity: {
            events: {
              onHover: { enable: true, mode: "grab" },
              resize: { enable: true }
            },
            modes: {
              grab: {
                distance: 150,
                links: { opacity: 0.28 }
              }
            }
          },
          detectRetina: true
        }
      });
    }


    const sendChat = document.getElementById("sendChat");
    const chatInput = document.getElementById("chatInput");
    const messages = document.getElementById("messages");

    function sendMessage() {
      const value = chatInput.value.trim();
      if (!value) return;
      const user = document.createElement("div");
      user.className = "msg user";
      user.textContent = value;
      messages.appendChild(user);
      chatInput.value = "";
      messages.scrollTop = messages.scrollHeight;

      setTimeout(() => {
        const bot = document.createElement("div");
        bot.className = "msg bot";
        bot.textContent = "Cảm ơn anh/chị. ChatStaff đã ghi nhận nhu cầu và sẽ chuyển thông tin cho chuyên viên TodoX.";
        messages.appendChild(bot);
        messages.scrollTop = messages.scrollHeight;
      }, 650);
    }
    sendChat.addEventListener("click", sendMessage);
    chatInput.addEventListener("keydown", e => { if (e.key === "Enter") sendMessage(); });

    const leadForm = document.getElementById("leadForm");
    const leadFormStatus = document.getElementById("leadFormStatus");

    function setLeadStatus(message, state = "info") {
      if (!leadFormStatus) return;
      leadFormStatus.className = `form-status is-${state}`;
      leadFormStatus.textContent = message;
    }

    function getUtmValue(name) {
      return new URLSearchParams(window.location.search).get(name) || "";
    }

    if (leadForm) {
      leadForm.addEventListener("submit", async event => {
        event.preventDefault();

        if (!leadForm.checkValidity()) {
          leadForm.reportValidity();
          return;
        }

        const config = window.TODOX_LANDING_CONFIG || {};
        const endpoint = (config.contactEndpoint || "").trim();
        const formData = new FormData(leadForm);
        const payload = {
          fullName: formData.get("fullName") || "",
          phone: formData.get("phone") || "",
          email: formData.get("email") || "",
          company: formData.get("company") || "",
          industry: formData.get("industry") || "",
          need: formData.get("need") || "",
          message: formData.get("message") || "",
          sourceUrl: window.location.href,
          utmSource: getUtmValue("utm_source"),
          utmMedium: getUtmValue("utm_medium"),
          utmCampaign: getUtmValue("utm_campaign"),
          utmContent: getUtmValue("utm_content"),
          utmTerm: getUtmValue("utm_term"),
          submittedAt: new Date().toISOString()
        };

        if (!endpoint) {
          setLeadStatus("Chế độ thử nghiệm: TodoX chưa cấu hình endpoint nhận tư vấn, nên dữ liệu đang được giữ lại trên trình duyệt và chưa được gửi đi.", "info");
          return;
        }

        const button = leadForm.querySelector("button[type='submit']");
        const oldHtml = button ? button.innerHTML : "";
        if (button) {
          button.disabled = true;
          button.innerHTML = '<i class="fa-solid fa-spinner fa-spin"></i> Đang gửi';
        }

        try {
          const response = await fetch(endpoint, {
            method: "POST",
            headers: { "Content-Type": "application/json" },
            body: JSON.stringify(payload)
          });

          if (!response.ok) {
            throw new Error(`HTTP ${response.status}`);
          }

          setLeadStatus("TodoX đã nhận thông tin tư vấn. Đội ngũ phụ trách sẽ liên hệ lại trong thời gian sớm nhất.", "success");
          leadForm.reset();
        } catch (error) {
          console.error("TodoX lead submit failed", error);
          setLeadStatus("Chưa gửi được thông tin. Vui lòng thử lại hoặc liên hệ TodoX qua hotline/email khi được cấu hình.", "error");
        } finally {
          if (button) {
            button.disabled = false;
            button.innerHTML = oldHtml;
          }
        }
      });
    }