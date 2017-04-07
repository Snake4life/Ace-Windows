#define _CRT_SECURE_NO_DEPRECATE
#pragma comment(lib, "lib\\detours.lib")

#include <Windows.h>

#include <string> // std::string
#include <fstream> // std::ifstream
#include <streambuf> // std::istreambuf_iterator

#include "include\detours.h"

// CEF API Headers
#include "include\capi\cef_app_capi.h"
#include "include\capi\cef_client_capi.h"
#include "include\capi\cef_browser_capi.h"
#include "include\capi\cef_request_handler_capi.h"

#define CEF_STR(name, tmp, contents) cef_string_t name = {}; const char* tmp = contents; f_cef_string_from_ascii(tmp, strlen(tmp), &name)

typedef int(CEF_CALLBACK* type_cef_string_utf16_to_utf8)(char16*, size_t, cef_string_utf8_t*);
typedef int(CEF_CALLBACK* type_cef_string_from_ascii)(const char*, size_t, void* s);
typedef void(CEF_CALLBACK* type_cef_string_userfree_free)(cef_string_userfree_t);
typedef int(CEF_CALLBACK* type_cef_initialize)(const struct _cef_main_args_t* args,
	const struct _cef_settings_t* settings, cef_app_t* application,
	void* windows_sandbox_info);
typedef int(CEF_CALLBACK* type_cef_browser_host_create_browser)(
	const cef_window_info_t* windowInfo, struct _cef_client_t* client,
	const cef_string_t* url, const struct _cef_browser_settings_t* settings,
	struct _cef_request_context_t* request_context);

type_cef_string_utf16_to_utf8 f_cef_string_utf16_to_utf8;
type_cef_string_from_ascii f_cef_string_from_ascii;
type_cef_string_userfree_free f_cef_string_userfree_free;
type_cef_initialize original_cef_initialize;
type_cef_browser_host_create_browser original_cef_browser_host_create_browser;

cef_request_handler_t*(CEF_CALLBACK* original_get_request_handler)(cef_client_t* self);
cef_resource_bundle_handler_t*(CEF_CALLBACK* original_get_resource_bundle_handler)(cef_app_t* self);

cef_return_value_t CEF_CALLBACK on_before_resource_load(
	struct _cef_request_handler_t* self, struct _cef_browser_t* browser,
	struct _cef_frame_t* frame, struct _cef_request_t* request,
	struct _cef_request_callback_t* callback)
{
	static bool did_initial_inject = true;

	cef_string_userfree_t url = request->get_url(request);
	cef_string_utf8_t str = {};
	f_cef_string_utf16_to_utf8(url->str, url->length, &str);

	char* initial_payload;
	char* load_payload;
	size_t len;

	_dupenv_s(&initial_payload, &len, "ACE_INITIAL_PAYLOAD");
	_dupenv_s(&load_payload, &len, "ACE_LOAD_PAYLOAD");

	if (strstr(str.str, "/graph.json")) {
		did_initial_inject = false;
	} else if (strstr(str.str, "/fe/") && strstr(str.str, "/index.html") && initial_payload && load_payload) {
		std::ifstream in(did_initial_inject ? load_payload : initial_payload);
		if (!in.bad()) {
			std::string code((std::istreambuf_iterator<char>(in)), std::istreambuf_iterator<char>());

			did_initial_inject = true;

			cef_string_t js_str = {};
			f_cef_string_from_ascii(code.c_str(), code.length(), &js_str);
			frame->execute_java_script(frame, &js_str, url, 0);
		}
	}

	f_cef_string_userfree_free(url);
	free(initial_payload);
	free(load_payload);

	return RV_CONTINUE;
}

cef_request_handler_t* CEF_CALLBACK get_request_handler(cef_client_t* self) {
	cef_request_handler_t null_handler = {};
	cef_request_handler_t* ret = original_get_request_handler ? original_get_request_handler(self) : &null_handler;
	ret->on_before_resource_load = on_before_resource_load;

	return ret;
}

int CEF_CALLBACK hooked_cef_browser_host_create_browser(
	const cef_window_info_t* windowInfo, struct _cef_client_t* client,
	const cef_string_t* url, const struct _cef_browser_settings_t* settings,
	struct _cef_request_context_t* request_context)
{
	original_get_request_handler = client->get_request_handler;
	client->get_request_handler = get_request_handler;

	return original_cef_browser_host_create_browser(windowInfo, client, url, settings, request_context);
}

int CEF_CALLBACK get_data_resource(
	struct _cef_resource_bundle_handler_t* self, int resource_id, void** data,
	size_t* data_size)
{
	return 0;
}

cef_resource_bundle_handler_t* CEF_CALLBACK get_resource_bundle_handler(_cef_app_t* self) {
	cef_resource_bundle_handler_t null_handler = {};
	cef_resource_bundle_handler_t* ret = original_get_resource_bundle_handler ? original_get_resource_bundle_handler(self) : &null_handler;
	ret->get_data_resource = get_data_resource;

	return ret;
}

void CEF_CALLBACK on_before_command_line_processing(
	struct _cef_app_t* self, const cef_string_t* process_type,
	struct _cef_command_line_t* command_line)
{
	CEF_STR(ignore_errors_cef, ignore_errors, "ignore-certificate-errors");
	command_line->append_switch(command_line, &ignore_errors_cef);

	CEF_STR(remote_debugging_cef, remote_debugging, "remote-debugging-port");
	CEF_STR(value_cef, value, "8888");
	command_line->append_switch_with_value(command_line, &remote_debugging_cef, &value_cef);
}

int CEF_CALLBACK hooked_cef_initialize(const struct _cef_main_args_t* args,
	const struct _cef_settings_t* settings, cef_app_t* application,
	void* windows_sandbox_info)
{
	original_get_resource_bundle_handler = application->get_resource_bundle_handler;
	application->get_resource_bundle_handler = get_resource_bundle_handler;
	application->on_before_command_line_processing = on_before_command_line_processing;

	return original_cef_initialize(args, settings, application, windows_sandbox_info);
}

void WINAPI DllThread(LPVOID lpParam) {
	HMODULE libcef = LoadLibrary("libcef.dll");
	f_cef_string_utf16_to_utf8 = (type_cef_string_utf16_to_utf8)GetProcAddress(libcef, "cef_string_utf16_to_utf8");
	f_cef_string_from_ascii = (type_cef_string_from_ascii)GetProcAddress(libcef, "cef_string_ascii_to_utf16");
	f_cef_string_userfree_free = (type_cef_string_userfree_free)GetProcAddress(libcef, "cef_string_userfree_utf16_free");
	original_cef_initialize = (type_cef_initialize)DetourFunction((PBYTE)
		GetProcAddress(libcef, "cef_initialize"),
		(PBYTE)hooked_cef_initialize);
	original_cef_browser_host_create_browser = (type_cef_browser_host_create_browser)DetourFunction((PBYTE)
		GetProcAddress(libcef, "cef_browser_host_create_browser"),
		(PBYTE)hooked_cef_browser_host_create_browser);
}

BOOL WINAPI DllMain(HINSTANCE hModule, DWORD dwAttatched, LPVOID lpvReserved) {
	switch (dwAttatched) {
	case DLL_PROCESS_ATTACH: {
		CreateThread(NULL, NULL, (LPTHREAD_START_ROUTINE)DllThread, NULL, NULL, NULL);
		break;
	}
	default:
		break;
	}
	return TRUE;
}