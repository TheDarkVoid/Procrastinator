var error;
var form;

$(document).ready(function(){
	error = $("form #formError");
	
	form = $("form").submit(function(e){
		e.preventDefault();
		console.log("sumbit");
		$.ajax({
			url:"/auth/login",
			type:"POST",
			data: form.serialize(),
			success:function(r){
				console.log("s");
				location.reload();
			},
			error:function(e)
			{
				error.text("Login information invalid");
				error.fadeIn();
			}
		});
	});
	
	
});
