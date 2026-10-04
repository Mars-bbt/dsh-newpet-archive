// dsh-newpet —— 鲸鱼娘桌宠插件(宿主半边,零侵入)。
// 1) 把包内 assets 目录以只读静态路由提供给浏览器(CSS / JS / generated/*.webp
//    / peek-calibration.json),供 client.js 在 DSH 窗口内注入鲸鱼娘。
// 2) 桌面桌宠:拉起包内 desktop-pet\WhaleOverlay.exe(独立透明置顶窗口),
//    DSH 最小化后仍在桌面上;插件卸载时一并结束。二者共用同一套立绘/状态。
import { createReadStream, existsSync, statSync } from "node:fs";
import { spawn } from "node:child_process";
import path from "node:path";
import { fileURLToPath } from "node:url";

/** Cordis 插件名(loader 诊断用)。 */
export const name = "dsh-newpet";

const HERE = path.dirname(fileURLToPath(import.meta.url));
const ASSETS_DIR = path.join(HERE, "..", "assets");
const DESKTOP_DIR = path.join(HERE, "..", "desktop-pet");
const DESKTOP_EXE = path.join(DESKTOP_DIR, "WhaleOverlay.exe");

const MIME = {
  ".css": "text/css; charset=utf-8",
  ".js": "text/javascript; charset=utf-8",
  ".json": "application/json; charset=utf-8",
  ".webp": "image/webp",
  ".png": "image/png",
  ".svg": "image/svg+xml",
};

/** 把请求参数 f 解析为 assets 目录内的安全路径;越界返回 null。 */
function safeResolve(rel) {
  const target = path.normalize(path.join(ASSETS_DIR, rel));
  if (target !== ASSETS_DIR && !target.startsWith(ASSETS_DIR + path.sep)) return null;
  return target;
}

/* ── 桌面桌宠(与插件绑定) ────────────────────────────────────────────
   包内 desktop-pet\WhaleOverlay.exe 是独立进程:透明置顶窗口 + 原生 PNG 立绘,
   DSH 最小化后依然悬浮在桌面上。EXE 自带单实例锁,重复拉起只会有一个。 */
function startDesktopPet(ctx) {
  try {
    if (!existsSync(DESKTOP_EXE)) {
      ctx.logger?.warn?.("dsh-newpet: 未找到 desktop-pet\\WhaleOverlay.exe,跳过桌面桌宠");
      return null;
    }
    const child = spawn(DESKTOP_EXE, [], {
      cwd: DESKTOP_DIR,
      detached: true,
      stdio: "ignore",
      windowsHide: false,
    });
    child.unref();
    ctx.logger?.info?.("dsh-newpet: 桌面桌宠已启动 pid=" + child.pid);
    return child;
  } catch (error) {
    ctx.logger?.warn?.("dsh-newpet: 桌面桌宠启动失败 " + error);
    return null;
  }
}

export async function apply(ctx) {
  // 桌面桌宠:随插件生效而启动,随插件卸载而结束(真正"绑定")
  const desktopPet = startDesktopPet(ctx);
  ctx.effect(
    () => () => {
      try {
        if (desktopPet && desktopPet.pid) process.kill(desktopPet.pid);
      } catch (error) {
        ctx.logger?.warn?.("dsh-newpet: 关闭桌面桌宠失败 " + error);
      }
    },
    "dsh-newpet: 桌面桌宠随插件卸载而结束"
  );

  // 必须走 inject —— apply 运行时刻 webserver 的 fiber 可能尚未创建
  ctx.inject(["webServer"], (wctx) => {
    const webServer = wctx.get("webServer");
    const dispose = webServer.register({
      kind: "exact",
      path: "/api/dsh-newpet/assets",
      handler: async (req, res) => {
        try {
          const url = new URL(req.url, "http://127.0.0.1");
          // f 的值可能带形如 ?v=3 的版本尾巴(表现层拼 URL 时追加),剥掉再解析
          const rel = (url.searchParams.get("f") ?? "").split("?")[0];
          const file = safeResolve(rel);
          if (file === null || !existsSync(file) || !statSync(file).isFile()) {
            res.writeHead(404, { "Content-Type": "text/plain; charset=utf-8" });
            res.end("not found");
            return;
          }
          const type = MIME[path.extname(file).toLowerCase()] ?? "application/octet-stream";
          res.writeHead(200, {
            "Content-Type": type,
            /* no-store: 表现层/核心 JS 的改动必须重启即生效,
               任何缓存(尤其 3600s max-age 的磁盘缓存)都会让补丁"看起来没实现"。 */
            "Cache-Control": "no-store",
          });
          createReadStream(file).pipe(res);
        } catch (error) {
          res.writeHead(400, { "Content-Type": "text/plain; charset=utf-8" });
          res.end(String(error?.message ?? error));
        }
      },
    });
    wctx.effect(() => dispose, "dsh-newpet: 静态资源路由");
  });

  ctx.logger?.info?.("dsh-newpet: 已挂载(窗口内看板娘 + 桌面悬浮桌宠)");
}
