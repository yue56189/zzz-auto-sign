/*
 * 米游社凭证提取脚本（在浏览器 F12 控制台执行）
 * ------------------------------------------------------------
 * 使用方法：
 *   1. 用 Chrome / Edge / Firefox 打开并登录 https://www.miyoushe.com/
 *      （或任意米游社 / 米哈游通行证页面，确保已登录）
 *   2. 按 F12 打开开发者工具，切到「控制台 / Console」标签页
 *   3. 若控制台提示 "允许粘贴"，按提示允许
 *   4. 把本文件全部内容复制粘贴到控制台，回车执行
 *   5. 脚本会把凭证 JSON 复制到剪贴板，同时打印在控制台
 *   6. 回到 CredentialGrabber 工具，粘贴即可
 *
 * 安全说明：
 *   - 本脚本只读取浏览器当前的 Cookie 并整理格式，不发送到任何服务器
 *   - 凭证只写入你自己的剪贴板，请勿分享给他人
 *   - Cookie 等同于登录态，请妥善保管
 */
(function () {
  'use strict';

  // 需要收集的 Cookie 名（同名多项时取第一个非空值）
  var WANTED = [
    'cookie_token', 'cookie_token_v2',
    'ltoken', 'ltoken_v2',
    'ltuid', 'ltuid_v2',
    'account_id', 'account_id_v2',
    'account_mid_v2', 'ltmid_v2',
    'stoken', 'stuid', 'mid'
  ];

  function parseCookies() {
    var out = {};
    var raw = document.cookie || '';
    if (!raw) return out;

    raw.split(';').forEach(function (piece) {
      var idx = piece.indexOf('=');
      if (idx <= 0) return;

      var k = piece.slice(0, idx).trim();
      var v = piece.slice(idx + 1).trim();
      if (!k || !v) return;

      if (out[k] === undefined) out[k] = v;
    });

    return out;
  }

  var all = parseCookies();

  var picked = {};
  WANTED.forEach(function (name) {
    var lowerTarget = name.toLowerCase();

    Object.keys(all).forEach(function (k) {
      if (k.toLowerCase() === lowerTarget && !picked[name]) {
        picked[name] = all[k];
      }
    });
  });

  // 拼装完整 cookie 串
  var cookieParts = Object.keys(picked)
    .filter(function (k) { return k.indexOf('stoken') !== 0; })
    .map(function (k) { return k + '=' + picked[k]; });

  var cookie = cookieParts.join('; ');

  var stoken = picked['stoken'] || '';
  var stuid =
    picked['stuid'] ||
    picked['ltuid'] ||
    picked['account_id'] ||
    picked['ltuid_v2'] ||
    picked['account_id_v2'] ||
    '';

  var mid =
    picked['mid'] ||
    picked['account_mid_v2'] ||
    picked['ltmid_v2'] ||
    '';

  var result = {
    cookie: cookie,
    stoken: stoken,
    stuid: stuid,
    mid: mid,
    _source: location.host,
    _time: new Date().toISOString()
  };

  var json = JSON.stringify(result, null, 2);

  // 打印到控制台
  console.log('%c========== 米游社凭证 ==========', 'color:#1a8cff;font-weight:bold');
  console.log(json);
  console.log('%c================================', 'color:#1a8cff;font-weight:bold');

  // 完整性检查
  var problems = [];
  if (!cookie && !stoken) problems.push('未读取到任何凭证，请确认已登录 www.miyoushe.com');
  if (!stoken) problems.push('未找到 stoken，建议在米游社网页版登录后再执行本脚本');
  if (!stuid) problems.push('未找到 stuid / ltuid，Cookie 可能不完整');
  if (stoken && stoken.indexOf('v2_') === 0 && !mid) {
    problems.push('检测到 v2 形态的 stoken 但缺少 mid，自动刷新会失败');
  }

  if (problems.length) {
    console.warn('提示：' + problems.join('；'));
  } else {
    console.log('%c凭证读取完整，可以复制到工具中。', 'color:#22aa55;font-weight:bold');
  }

  // 复制到剪贴板
  function copy(text) {
    if (navigator.clipboard && navigator.clipboard.writeText) {
      return navigator.clipboard.writeText(text);
    }

    // 兼容旧浏览器 / 非安全上下文
    return new Promise(function (resolve, reject) {
      try {
        var ta = document.createElement('textarea');
        ta.value = text;
        ta.style.position = 'fixed';
        ta.style.opacity = '0';
        document.body.appendChild(ta);
        ta.select();
        document.execCommand('copy');
        document.body.removeChild(ta);
        resolve();
      } catch (e) {
        reject(e);
      }
    });
  }

  copy(json)
    .then(function () {
      console.log('%c已复制到剪贴板。', 'color:#22aa55;font-weight:bold');
      alert('凭证已复制到剪贴板，请粘贴到 CredentialGrabber 工具中。');
    })
    .catch(function () {
      console.warn('自动复制失败，请手动选中上面的 JSON 并复制。');
      alert('自动复制失败，请手动复制控制台中的 JSON。');
    });

  // 便于在控制台直接取用
  window.__MYS_CREDENTIAL__ = result;
})();
